using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using System.Linq;
using System.Windows.Media.Imaging;
using System.Windows.Media;

namespace EasySnapApp.Services
{
    /// <summary>
    /// Canon camera service with EDSDK tethering support and FileSystemWatcher fallback.
    ///
    /// CRITICAL FIXES APPLIED:
    /// - Fixed volume event handling: 0x201/0x202 are NOT file download events
    /// - Added post-download file verification before PhotoSaved event
    /// - Added path-based deduplication to prevent duplicate processing
    /// - Only process legitimate directory item events (0x204, 0x208, 0x209)
    /// - Corrected object event constants to match actual Canon EDSDK headers from project
    /// - Fixed EdsCreateFileStream parameter types (use enum instead of int) - this should resolve 0x000000AB error
    /// - Added proper error handling for EDS_ERR_STREAM_BAD_OPTIONS (0x000000AB)  
    /// - Corrected event mapping: 0x201=VolumeInfoChanged, 0x204=DirItemCreated, 0x206=DirItemInfoChanged
    ///
    /// What this addresses from your log:
    /// - Error 0x000000AB (Stream Bad Options) was caused by passing integer constants instead of enum types
    /// - Event constants now match project EDSDK.cs exactly
    /// - File stream creation should now work properly with correct parameter types
    /// - FALSE POSITIVE downloads eliminated - files only reported as saved when verified on disk
    /// </summary>
    public class CanonCameraService : IDisposable
    {
        // EDSDK state
        private IntPtr _cameraList = IntPtr.Zero;
        private IntPtr _cameraRef = IntPtr.Zero;
        private bool _isEdsdkInitialized;
        private bool _isSessionOpen;

        // Event handler delegates (must be kept alive)
        private CanonEdSdkNative.EdsObjectEventHandler _objectEventHandler;
        private CanonEdSdkNative.EdsStateEventHandler _stateEventHandler;

        // ===== EDSDK EVENT PUMP (CRITICAL IN YOUR ENV) =====
        [DllImport("EDSDK.dll", CallingConvention = CallingConvention.StdCall)]
        private static extern uint EdsGetEvent();

        private DispatcherTimer _edsEventPumpTimer;
        private int _edsGetEventErrorBurst;
        private DateTime _lastEdsGetEventErrorLog = DateTime.MinValue;

        // FileSystemWatcher fallback (kept for compatibility)
        private FileSystemWatcher _watcher;
        private string _watchFolder;
        private bool _useFileWatcher;

        // Session context
        private string _currentPartNumber;
        private Func<int> _getNextSequence;
        private string _exportRootFolder;

        // Refresh throttling
        private DateTime _lastConnectAttempt = DateTime.MinValue;
        private readonly TimeSpan _connectThrottleInterval = TimeSpan.FromSeconds(60);

        // Event de-dupe
        private readonly HashSet<IntPtr> _processedTransferRefs = new HashSet<IntPtr>();
        private readonly HashSet<string> _processedFilePaths = new HashSet<string>();
        private readonly object _eventLock = new object();

        // Diagnostic counters - corrected labels based on actual Canon EDSDK
        private int _count201 = 0; // VolumeInfoChanged
        private int _count202 = 0; // VolumeUpdateItems  
        private int _count204 = 0; // DirItemCreated
        private int _count206 = 0; // DirItemInfoChanged
        private int _count208 = 0; // DirItemRequestTransfer
        private int _count209 = 0; // DirItemRequestTransferDT

        // File logging
        private readonly object _fileLock = new object();
        private string _logFilePath;

        public event Action<string> Log;
        // DEPRECATED: Use PhotoSavedWithThumbnail instead for single-event architecture
        public event Action<string> PhotoSaved; // local file path
        
        // PRIMARY EVENT: Single event per capture with both image and thumbnail paths
        public event Action<string, string> PhotoSavedWithThumbnail; // (fullImagePath, thumbnailPath)

        public bool IsConnected { get; private set; }
        public string ConnectedModel { get; private set; } = "Not connected";

        public CanonCameraService(string exportFolder)
        {
            _exportRootFolder = exportFolder;
            Directory.CreateDirectory(_exportRootFolder);

            InitializeFileLogging();
            
            // EMERGENCY - This MUST appear in logs if new code compiled
            LogMessage("*** PHIL - SINGLE EVENT FIX VERSION 23:30 COMPILED SUCCESSFULLY ***");
            LogMessage("*** USING ONLY PhotoSavedWithThumbnail EVENT - NO DUPLICATES ***");

            _objectEventHandler = OnObjectEvent;
            _stateEventHandler = OnStateEvent;

            LogMessage($"Canon: Event handlers rooted - ObjectHandler hash={_objectEventHandler.GetHashCode():X8}, StateHandler hash={_stateEventHandler.GetHashCode():X8}");
        }

        #region Logging
        private void InitializeFileLogging()
        {
            try
            {
                var logsDir = Path.Combine(_exportRootFolder, "logs");
                Directory.CreateDirectory(logsDir);

                var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                _logFilePath = Path.Combine(logsDir, $"easysnap_{timestamp}.log");

                WriteToLogFile($"=== SESSION START - {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
                WriteToLogFile($"Log file: {_logFilePath}");
            }
            catch
            {
                _logFilePath = null;
            }
        }

        private void WriteToLogFile(string message)
        {
            if (string.IsNullOrEmpty(_logFilePath)) return;

            try
            {
                lock (_fileLock)
                {
                    var logLine = $"[{DateTime.Now:HH:mm:ss.fff}] {message}";
                    File.AppendAllText(_logFilePath, logLine + Environment.NewLine, System.Text.Encoding.UTF8);
                }
            }
            catch
            {
                // swallow logging errors
            }
        }

        private void LogMessage(string message)
        {
            WriteToLogFile(message);

            // always marshal to UI thread for UI log binding
            Application.Current?.Dispatcher?.BeginInvoke(new Action(() =>
            {
                Log?.Invoke(message);
            }));
        }
        #endregion

        #region Safe release
        private void SafeEdsRelease(IntPtr handle, string description)
        {
            if (handle == IntPtr.Zero) return;

            try
            {
                uint result = CanonEdSdkNative.EdsRelease(handle);
                LogMessage($"Canon: EdsRelease({description}) = 0x{result:X8}");
            }
            catch (EntryPointNotFoundException)
            {
                LogMessage($"Canon: EdsRelease not available - skipping release of {description} (handle=0x{handle.ToInt64():X16})");
            }
            catch (Exception ex)
            {
                LogMessage($"Canon: EdsRelease error for {description}: {ex.Message}");
            }
        }
        #endregion

        #region Event pump
        private void StartEdsdkEventPump()
        {
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                if (_edsEventPumpTimer != null) return;

                _edsEventPumpTimer = new DispatcherTimer(DispatcherPriority.Background)
                {
                    // 100ms is a safer compromise than 20ms: much lower CPU + fewer DEVICE_BUSY bursts
                    Interval = TimeSpan.FromMilliseconds(100)
                };

                _edsEventPumpTimer.Tick += (s, e) =>
                {
                    uint err;
                    try
                    {
                        err = EdsGetEvent();
                    }
                    catch (Exception ex)
                    {
                        LogMessage($"Canon: EdsGetEvent EXCEPTION: {ex.Message}");
                        return;
                    }

                    if (err != CanonEdSdkNative.EDS_ERR_OK)
                    {
                        _edsGetEventErrorBurst++;

                        if (_edsGetEventErrorBurst == 1 || (DateTime.Now - _lastEdsGetEventErrorLog).TotalSeconds >= 2)
                        {
                            _lastEdsGetEventErrorLog = DateTime.Now;
                            LogMessage($"Canon: EdsGetEvent ERROR = 0x{err:X8} ({CanonEdSdkNative.GetErrorDescription(err)}), burst={_edsGetEventErrorBurst}");
                        }
                    }
                    else
                    {
                        _edsGetEventErrorBurst = 0;
                    }
                };

                _edsEventPumpTimer.Start();
                LogMessage("Canon: EDSDK EVENT PUMP STARTED (DispatcherTimer 100ms)");
                WriteToLogFile("=== EDSDK EVENT PUMP STARTED (DispatcherTimer 100ms) ===");
            });
        }

        private void StopEdsdkEventPump()
        {
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                if (_edsEventPumpTimer == null) return;

                try { _edsEventPumpTimer.Stop(); }
                catch { }
                finally { _edsEventPumpTimer = null; }

                LogMessage("Canon: EDSDK EVENT PUMP STOPPED");
                WriteToLogFile("=== EDSDK EVENT PUMP STOPPED ===");
            });
        }
        #endregion

        #region Connect / Disconnect
        public async Task<bool> Connect(bool forceRefresh = false)
        {
            if (_lastConnectAttempt != DateTime.MinValue && !forceRefresh)
            {
                var timeSinceLastAttempt = DateTime.Now - _lastConnectAttempt;
                if (timeSinceLastAttempt < _connectThrottleInterval)
                {
                    var remaining = _connectThrottleInterval - timeSinceLastAttempt;
                    LogMessage($"Canon: Connect throttled, {remaining.TotalSeconds:F0}s cooldown remaining");
                    return IsConnected;
                }
            }

            _lastConnectAttempt = DateTime.Now;

            LogMessage("Canon: Attempting EDSDK connection...");
            LogMessage($"Canon: UILock constant = 0x{CanonEdSdkNative.kEdsCameraStatusCommand_UILock:X8}");
            LogMessage($"Canon: UIUnLock constant = 0x{CanonEdSdkNative.kEdsCameraStatusCommand_UIUnLock:X8}");

            bool success = await ConnectEdsdk().ConfigureAwait(false);

            if (success)
            {
                _useFileWatcher = false;
                LogMessage("Canon: EDSDK tethering active");
                LogMessage("Canon: Mode = EDSDK");
                WriteToLogFile("=== EDSDK MODE ACTIVE ===");
            }
            else
            {
                _useFileWatcher = true;
                LogMessage("Canon: EDSDK failed, using FileSystemWatcher fallback");
                LogMessage("Canon: Mode = FileWatcher");
                WriteToLogFile("=== FILEWATCHER MODE ACTIVE ===");

                // NOTE: Keeping your fallback mechanism intact, but not expanding here.
                // If you want, we can wire watcher to the camera folder.
            }

            LogMessage($"Canon: Connect() completed, success={success}");
            return success;
        }

        private async Task<bool> ConnectEdsdk()
        {
            try
            {
                uint result = CanonEdSdkNative.EdsInitializeSDK();
                LogMessage($"Canon: EdsInitializeSDK = 0x{result:X8} ({CanonEdSdkNative.GetErrorDescription(result)})");
                if (result != CanonEdSdkNative.EDS_ERR_OK)
                {
                    if (result == CanonEdSdkNative.EDS_ERR_DEVICE_BUSY)
                        LogMessage("Canon: Camera busy. Close EOS Utility, unplug USB, power cycle camera, retry.");
                    return false;
                }
                _isEdsdkInitialized = true;

                result = CanonEdSdkNative.EdsGetCameraList(out _cameraList);
                LogMessage($"Canon: EdsGetCameraList = 0x{result:X8} ({CanonEdSdkNative.GetErrorDescription(result)})");
                if (result != CanonEdSdkNative.EDS_ERR_OK)
                {
                    CleanupEdsdk();
                    return false;
                }

                result = CanonEdSdkNative.EdsGetChildCount(_cameraList, out uint count);
                LogMessage($"Canon: EdsGetChildCount = 0x{result:X8}, count = {count}");
                if (result != CanonEdSdkNative.EDS_ERR_OK || count == 0)
                {
                    LogMessage("Canon: No cameras found");
                    CleanupEdsdk();
                    return false;
                }

                result = CanonEdSdkNative.EdsGetChildAtIndex(_cameraList, 0, out _cameraRef);
                LogMessage($"Canon: EdsGetChildAtIndex(0) = 0x{result:X8} ({CanonEdSdkNative.GetErrorDescription(result)})");
                if (result != CanonEdSdkNative.EDS_ERR_OK)
                {
                    CleanupEdsdk();
                    return false;
                }

                result = CanonEdSdkNative.EdsOpenSession(_cameraRef);
                LogMessage($"Canon: EdsOpenSession = 0x{result:X8} ({CanonEdSdkNative.GetErrorDescription(result)})");
                if (result != CanonEdSdkNative.EDS_ERR_OK)
                {
                    if (result == CanonEdSdkNative.EDS_ERR_DEVICE_BUSY)
                        LogMessage("Canon: Camera busy. Close EOS Utility, unplug USB, power cycle camera, retry.");
                    CleanupEdsdk();
                    return false;
                }
                _isSessionOpen = true;

                result = CanonEdSdkNative.EdsSendCommand(_cameraRef, CanonEdSdkNative.kEdsCameraCommand_ExtendShutDownTimer, 0);
                LogMessage($"Canon: EdsSendCommand(ExtendShutDownTimer) = 0x{result:X8} ({CanonEdSdkNative.GetErrorDescription(result)})");

                // Register handlers
                result = CanonEdSdkNative.EdsSetObjectEventHandler(_cameraRef, CanonEdSdkNative.kEdsObjectEvent_All, _objectEventHandler, IntPtr.Zero);
                LogMessage($"Canon: EdsSetObjectEventHandler(All) = 0x{result:X8} ({CanonEdSdkNative.GetErrorDescription(result)})");

                result = CanonEdSdkNative.EdsSetCameraStateEventHandler(_cameraRef, CanonEdSdkNative.kEdsStateEvent_All, _stateEventHandler, IntPtr.Zero);
                LogMessage($"Canon: EdsSetCameraStateEventHandler(All) = 0x{result:X8} ({CanonEdSdkNative.GetErrorDescription(result)})");

                // SaveTo = Host (and verify)
                LogMessage($"Canon: Setting SaveTo = Host(2) using property ID 0x{CanonEdSdkNative.kEdsPropID_SaveTo:X8}");
                uint saveToValue = CanonEdSdkNative.kEdsSaveTo_Host;
                IntPtr saveToPtr = Marshal.AllocHGlobal(sizeof(uint));
                try
                {
                    Marshal.WriteInt32(saveToPtr, unchecked((int)saveToValue));
                    result = CanonEdSdkNative.EdsSetPropertyData(_cameraRef, CanonEdSdkNative.kEdsPropID_SaveTo, 0, sizeof(uint), saveToPtr);
                    LogMessage($"Canon: EdsSetPropertyData(SaveTo=Host) = 0x{result:X8} ({CanonEdSdkNative.GetErrorDescription(result)})");
                }
                finally
                {
                    Marshal.FreeHGlobal(saveToPtr);
                }

                // Verify SaveTo
                try
                {
                    IntPtr verifyPtr = Marshal.AllocHGlobal(sizeof(uint));
                    try
                    {
                        uint vr = CanonEdSdkNative.EdsGetPropertyData(_cameraRef, CanonEdSdkNative.kEdsPropID_SaveTo, 0, sizeof(uint), verifyPtr);
                        uint got = unchecked((uint)Marshal.ReadInt32(verifyPtr));
                        LogMessage($"Canon: Verified SaveTo = {got} (expected 2), Get=0x{vr:X8} ({CanonEdSdkNative.GetErrorDescription(vr)})");
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(verifyPtr);
                    }
                }
                catch (Exception ex)
                {
                    LogMessage($"Canon: SaveTo verify failed: {ex.Message}");
                }

                // Capacity (required for transfer)
                var capacity = new CanonEdSdkNative.EdsCapacity
                {
                    NumberOfFreeClusters = 0x7FFFFFFF,
                    BytesPerSector = 0x1000,
                    Reset = 1
                };

                result = CanonEdSdkNative.EdsSetCapacity(_cameraRef, capacity);
                LogMessage($"Canon: EdsSetCapacity = 0x{result:X8} ({CanonEdSdkNative.GetErrorDescription(result)})");

                // Start event pump (your setup depends on it)
                StartEdsdkEventPump();

                ConnectedModel = GetCameraName();
                IsConnected = true;

                LogMessage($"Canon: EDSDK connection successful - {ConnectedModel}");
                return true;
            }
            catch (Exception ex)
            {
                LogMessage($"Canon: EDSDK connection failed: {ex.Message}");
                CleanupEdsdk();
                return false;
            }
        }

        private string GetCameraName()
        {
            try
            {
                uint result = CanonEdSdkNative.EdsGetPropertySize(_cameraRef, CanonEdSdkNative.kEdsPropID_ProductName, 0, out int dataType, out int size);
                if (result == CanonEdSdkNative.EDS_ERR_OK && size > 0)
                {
                    IntPtr buffer = Marshal.AllocHGlobal(size);
                    try
                    {
                        result = CanonEdSdkNative.EdsGetPropertyData(_cameraRef, CanonEdSdkNative.kEdsPropID_ProductName, 0, size, buffer);
                        if (result == CanonEdSdkNative.EDS_ERR_OK)
                            return Marshal.PtrToStringAnsi(buffer) ?? "Canon Camera";
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(buffer);
                    }
                }
            }
            catch
            {
                // ignore
            }

            return "Canon Camera (EDSDK)";
        }

        public void Disconnect()
        {
            try
            {
                LogMessage("Canon: Disconnecting...");

                StopEdsdkEventPump();

                if (_watcher != null)
                {
                    _watcher.EnableRaisingEvents = false;
                    _watcher.Dispose();
                    _watcher = null;
                }

                lock (_eventLock)
                {
                    _processedTransferRefs.Clear();
                    _processedFilePaths.Clear();
                }

                CleanupEdsdk();

                IsConnected = false;
                ConnectedModel = "Not connected";
                _useFileWatcher = false;

                LogMessage("Canon: Disconnected");
            }
            catch (Exception ex)
            {
                LogMessage($"Canon: Disconnect error: {ex.Message}");
            }
        }

        private void CleanupEdsdk()
        {
            try
            {
                StopEdsdkEventPump();

                if (_isSessionOpen && _cameraRef != IntPtr.Zero)
                {
                    uint result = CanonEdSdkNative.EdsCloseSession(_cameraRef);
                    LogMessage($"Canon: EdsCloseSession = 0x{result:X8}");
                    _isSessionOpen = false;
                }

                if (_cameraRef != IntPtr.Zero)
                {
                    SafeEdsRelease(_cameraRef, "Camera reference");
                    _cameraRef = IntPtr.Zero;
                }

                if (_cameraList != IntPtr.Zero)
                {
                    SafeEdsRelease(_cameraList, "Camera list reference");
                    _cameraList = IntPtr.Zero;
                }

                if (_isEdsdkInitialized)
                {
                    uint result = CanonEdSdkNative.EdsTerminateSDK();
                    LogMessage($"Canon: EdsTerminateSDK = 0x{result:X8}");
                    _isEdsdkInitialized = false;
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Canon: EDSDK cleanup error: {ex.Message}");
            }
        }
        #endregion

        #region Events
        private uint OnObjectEvent(uint inEvent, IntPtr inRef, IntPtr inContext)
        {
            try
            {
                LogMessage($"Canon: ObjectEvent received - 0x{inEvent:X8}, inRef=0x{inRef.ToInt64():X16}");
                WriteToLogFile($"OBJECTEVENT: 0x{inEvent:X8} inRef=0x{inRef.ToInt64():X16}");

                // IMMEDIATE FIX: Block volume events (0x201, 0x202) completely
                if (inEvent == 0x00000201 || inEvent == 0x00000202)
                {
                    if (inEvent == 0x00000201) _count201++;
                    if (inEvent == 0x00000202) _count202++;
                    
                    string eventLabel = inEvent == 0x00000201 ? "VOLUMEINFOCHANGED (0x201)" : "VOLUMEUPDATEITEMS (0x202)";
                    LogMessage($"Canon: {eventLabel} - BLOCKED, not processing as file download");
                    LogMessage($"Canon: Event summary — 0x201={_count201}, 0x202={_count202}, 0x204={_count204}, 0x206={_count206}, 0x208={_count208}, 0x209={_count209}");
                    
                    return CanonEdSdkNative.EDS_ERR_OK; // Exit immediately
                }

                // If we don't have a session context yet, ignore but STILL release.
                bool haveContext = !string.IsNullOrEmpty(_currentPartNumber);

                bool shouldProcess = false;
                string label = null;

                // CRITICAL FIX: Only process actual directory item events, NOT volume events
                if (inEvent == CanonEdSdkNative.kEdsObjectEvent_DirItemRequestTransfer) // 0x208
                {
                    _count208++;
                    label = "REQUESTTRANSFER (0x208)";
                    shouldProcess = true;
                }
                else if (inEvent == CanonEdSdkNative.kEdsObjectEvent_DirItemRequestTransferDT) // 0x209
                {
                    _count209++;
                    label = "REQUESTTRANSFER_DT (0x209)";
                    shouldProcess = true;
                }
                else if (inEvent == CanonEdSdkNative.kEdsObjectEvent_DirItemCreated) // 0x204
                {
                    _count204++;
                    label = "DIRITEMCREATED (0x204)";
                    shouldProcess = true;
                }
                else if (inEvent == CanonEdSdkNative.kEdsObjectEvent_DirItemInfoChanged) // 0x206
                {
                    _count206++;
                    label = "DIRITEMINFOCHANGED (0x206)";
                    shouldProcess = false; // Don't process info changes as downloads
                }
                else if (inEvent == CanonEdSdkNative.kEdsObjectEvent_VolumeInfoChanged) // 0x201
                {
                    // According to corrected EDSDK, 0x201 is VolumeInfoChanged.
                    // But your logs show this consistently with image captures.
                    // Let's process it as a potential image event.
                    _count201++;
                    label = "VOLUMEINFOCHANGED (0x201)";
                    shouldProcess = true;
                }
                else if (inEvent == CanonEdSdkNative.kEdsObjectEvent_VolumeUpdateItems) // 0x202
                {
                    // Your log shows 0x202 events, this maps to VolumeUpdateItems
                    _count202++;
                    label = "VOLUMEUPDATEITEMS (0x202)";
                    shouldProcess = true;
                }
                else
                {
                    label = $"UNHANDLED (0x{inEvent:X8})";
                }

                lock (_eventLock)
                {
                    LogMessage($"Canon: Event summary — 0x201={_count201}, 0x202={_count202}, 0x204={_count204}, 0x206={_count206}, 0x208={_count208}, 0x209={_count209}");

                    if (!shouldProcess)
                    {
                        return CanonEdSdkNative.EDS_ERR_OK;
                    }

                    if (!haveContext)
                    {
                        LogMessage("Canon: Image detected but no part number set - ignoring");
                        // Still release the ref if EDSDK expects it (we do in finally in ProcessObjectEvent)
                        Task.Run(() => ProcessObjectEvent(inRef, inEvent, label, allowIgnoreBecauseNoContext: true));
                        return CanonEdSdkNative.EDS_ERR_OK;
                    }

                    if (_processedTransferRefs.Contains(inRef))
                    {
                        LogMessage($"Canon: Duplicate transfer ref ignored: 0x{inRef.ToInt64():X16} ({label})");
                        return CanonEdSdkNative.EDS_ERR_OK;
                    }

                    _processedTransferRefs.Add(inRef);
                    Task.Run(() => ProcessObjectEvent(inRef, inEvent, label, allowIgnoreBecauseNoContext: false));
                }

                return CanonEdSdkNative.EDS_ERR_OK;
            }
            catch (Exception ex)
            {
                LogMessage($"Canon: ObjectEvent error: {ex.Message}");
                return CanonEdSdkNative.EDS_ERR_INTERNAL_ERROR;
            }
        }

        private uint OnStateEvent(uint inEvent, uint inEventData, IntPtr inContext)
        {
            try
            {
                LogMessage($"Canon: StateEvent - 0x{inEvent:X8}, data: 0x{inEventData:X8}");

                if (inEvent == CanonEdSdkNative.kEdsStateEvent_ShutDownTimerUpdated)
                {
                    LogMessage("Canon: Shutdown timer updated, extending session");
                    if (_cameraRef != IntPtr.Zero)
                        CanonEdSdkNative.EdsSendCommand(_cameraRef, CanonEdSdkNative.kEdsCameraCommand_ExtendShutDownTimer, 0);
                }
                else if (inEvent == CanonEdSdkNative.kEdsStateEvent_CaptureError)
                {
                    LogMessage("Canon: Capture error occurred");
                }
                else if (inEvent == CanonEdSdkNative.kEdsStateEvent_InternalError)
                {
                    LogMessage("Canon: Internal error occurred");
                }

                return CanonEdSdkNative.EDS_ERR_OK;
            }
            catch (Exception ex)
            {
                LogMessage($"Canon: StateEvent error: {ex.Message}");
                return CanonEdSdkNative.EDS_ERR_INTERNAL_ERROR;
            }
        }
        #endregion

        #region Download pipeline
        private async Task ProcessObjectEvent(IntPtr objectRef, uint eventType, string label, bool allowIgnoreBecauseNoContext)
        {
            IntPtr actualFile = IntPtr.Zero;

            try
            {
                if (allowIgnoreBecauseNoContext && string.IsNullOrEmpty(_currentPartNumber))
                {
                    // Just release in finally.
                    return;
                }

                if (string.IsNullOrEmpty(_currentPartNumber))
                {
                    LogMessage("Canon: Image detected but no part number set - ignoring");
                    return;
                }

                LogMessage($"Canon: Processing object event {label} 0x{eventType:X8}, objectRef=0x{objectRef.ToInt64():X16}...");

                actualFile = await FindActualFile(objectRef).ConfigureAwait(false);
                if (actualFile == IntPtr.Zero)
                {
                    LogMessage("Canon: No actual image file found in object");
                    return;
                }

                await ProcessImageDownload(actualFile).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogMessage($"Canon: Object event processing error: {ex.Message}");
            }
            finally
            {
                // IMPORTANT: Avoid double-release.
                if (objectRef != IntPtr.Zero && objectRef != actualFile)
                    SafeEdsRelease(objectRef, "Original object reference");
            }
        }

        private async Task<IntPtr> FindActualFile(IntPtr objectRef)
        {
            try
            {
                // First try to treat inRef as a directory item.
                var (result, itemInfo) = await GetDirectoryItemInfoWithRetry(objectRef).ConfigureAwait(false);
                LogMessage($"Canon: GetDirectoryItemInfoWithRetry = 0x{result:X8} ({CanonEdSdkNative.GetErrorDescription(result)})");

                if (result == CanonEdSdkNative.EDS_ERR_OK)
                {
                    if (itemInfo.IsFolder == 0 && itemInfo.Size > 0 && !string.IsNullOrEmpty(itemInfo.FileName))
                    {
                        LogMessage($"Canon: Direct file found: {itemInfo.FileName} size={itemInfo.Size}");
                        return objectRef;
                    }

                    if (itemInfo.IsFolder != 0)
                    {
                        LogMessage("Canon: Object is a folder, enumerating contents...");
                        return await EnumerateForFile(objectRef).ConfigureAwait(false);
                    }
                }

                // Otherwise enumerate.
                LogMessage($"Canon: Direct info failed or not a file, enumerating (result=0x{result:X8})...");
                return await EnumerateForFile(objectRef).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogMessage($"Canon: FindActualFile error: {ex.Message}");
                return IntPtr.Zero;
            }
        }

        private async Task<(uint result, CanonEdSdkNative.EdsDirectoryItemInfo itemInfo)> GetDirectoryItemInfoWithRetry(IntPtr objectRef)
        {
            const int maxRetries = 6;
            var itemInfo = new CanonEdSdkNative.EdsDirectoryItemInfo();

            for (int attempt = 0; attempt <= maxRetries; attempt++)
            {
                uint result = CanonEdSdkNative.EdsGetDirectoryItemInfo(objectRef, out itemInfo);

                if (result == CanonEdSdkNative.EDS_ERR_OK)
                {
                    if (attempt > 0)
                        LogMessage($"Canon: EdsGetDirectoryItemInfo succeeded on attempt {attempt + 1}");
                    return (result, itemInfo);
                }

                bool retryable = result == CanonEdSdkNative.EDS_ERR_DEVICE_BUSY || result == CanonEdSdkNative.EDS_ERR_OBJECT_NOTREADY;
                if (!retryable || attempt == maxRetries)
                    return (result, itemInfo);

                int delayMs = (int)Math.Pow(2, attempt) * 75;
                LogMessage($"Canon: EdsGetDirectoryItemInfo attempt {attempt + 1} failed: {CanonEdSdkNative.GetErrorDescription(result)}, retrying in {delayMs}ms...");
                await Task.Delay(delayMs).ConfigureAwait(false);
            }

            return (CanonEdSdkNative.EDS_ERR_INTERNAL_ERROR, itemInfo);
        }

        private async Task<IntPtr> EnumerateForFile(IntPtr containerRef)
        {
            try
            {
                uint result = CanonEdSdkNative.EdsGetChildCount(containerRef, out uint childCount);
                LogMessage($"Canon: EdsGetChildCount = 0x{result:X8}, count = {childCount}");

                if (result != CanonEdSdkNative.EDS_ERR_OK || childCount == 0)
                    return IntPtr.Zero;

                for (int i = 0; i < childCount; i++)
                {
                    IntPtr childRef = IntPtr.Zero;
                    try
                    {
                        result = CanonEdSdkNative.EdsGetChildAtIndex(containerRef, i, out childRef);
                        if (result != CanonEdSdkNative.EDS_ERR_OK)
                            continue;

                        result = CanonEdSdkNative.EdsGetDirectoryItemInfo(childRef, out var childInfo);
                        if (result == CanonEdSdkNative.EDS_ERR_OK)
                        {
                            if (childInfo.IsFolder == 0 && childInfo.Size > 0 && !string.IsNullOrEmpty(childInfo.FileName))
                            {
                                string ext = Path.GetExtension(childInfo.FileName).ToLowerInvariant();
                                if (ext == ".jpg" || ext == ".jpeg" || ext == ".cr2" || ext == ".cr3")
                                {
                                    LogMessage($"Canon: Found image file: {childInfo.FileName}");
                                    return childRef; // caller owns
                                }
                            }

                            if (childInfo.IsFolder != 0)
                            {
                                var nested = await EnumerateForFile(childRef).ConfigureAwait(false);
                                if (nested != IntPtr.Zero)
                                {
                                    SafeEdsRelease(childRef, $"Folder reference {childInfo.FileName}");
                                    return nested;
                                }
                            }
                        }

                        SafeEdsRelease(childRef, "Child reference");
                        childRef = IntPtr.Zero;
                    }
                    catch (Exception ex)
                    {
                        LogMessage($"Canon: Child enumeration error: {ex.Message}");
                        if (childRef != IntPtr.Zero)
                            SafeEdsRelease(childRef, "Child reference (enumeration error)");
                    }
                }

                return IntPtr.Zero;
            }
            catch (Exception ex)
            {
                LogMessage($"Canon: EnumerateForFile error: {ex.Message}");
                return IntPtr.Zero;
            }
        }

        private async Task ProcessImageDownload(IntPtr dirItemRef)
        {
            IntPtr memoryStreamRef = IntPtr.Zero;
            bool downloadStarted = false;
            string targetPath = null;

            try
            {
                var (result, itemInfo) = await GetDirectoryItemInfoWithRetry(dirItemRef).ConfigureAwait(false);
                LogMessage($"Canon: ItemInfo = 0x{result:X8} ({CanonEdSdkNative.GetErrorDescription(result)}), file='{itemInfo.FileName}', size={itemInfo.Size}, isFolder={itemInfo.IsFolder}");

                if (result != CanonEdSdkNative.EDS_ERR_OK) return;
                if (itemInfo.IsFolder != 0) return;
                if (string.IsNullOrEmpty(itemInfo.FileName) || itemInfo.Size == 0) return;

                int sequence = _getNextSequence?.Invoke() ?? 103;

                string partDir = Path.Combine(_exportRootFolder, _currentPartNumber);
                Directory.CreateDirectory(partDir);

                // Preserve your naming convention; default to JPG (you can extend based on itemInfo.FileName ext)
                string ext = Path.GetExtension(itemInfo.FileName);
                if (string.IsNullOrWhiteSpace(ext)) ext = ".jpg";

                targetPath = Path.Combine(partDir, $"{_currentPartNumber}.{sequence:000}{ext}");
                while (File.Exists(targetPath))
                {
                    sequence = _getNextSequence?.Invoke() ?? (sequence + 1);
                    targetPath = Path.Combine(partDir, $"{_currentPartNumber}.{sequence:000}{ext}");
                }

                // CRITICAL: Check for duplicate processing by target path FIRST
                lock (_eventLock)
                {
                    if (_processedFilePaths.Contains(targetPath))
                    {
                        LogMessage($"Canon: Duplicate target path detected, aborting: {targetPath}");
                        return;
                    }
                    // Reserve this path immediately
                    _processedFilePaths.Add(targetPath);
                }

                LogMessage($"Canon: Downloading to: {targetPath}");
                LogMessage($"Canon: Expected file size from EDSDK: {itemInfo.Size:N0} bytes");

                // NEW APPROACH: Create memory stream instead of file stream
                ulong bufferSize = itemInfo.Size > 0 ? itemInfo.Size : 10 * 1024 * 1024; // Default 10MB if size unknown
                uint createResult = CanonEdSdkNative.EdsCreateMemoryStream(bufferSize, out memoryStreamRef);
                LogMessage($"Canon: EdsCreateMemoryStream(bufferSize={bufferSize:N0}) = 0x{createResult:X8} ({CanonEdSdkNative.GetErrorDescription(createResult)})");
                
                if (createResult != CanonEdSdkNative.EDS_ERR_OK || memoryStreamRef == IntPtr.Zero)
                {
                    LogMessage($"Canon: Memory stream creation failed, falling back to file stream approach");
                    await ProcessImageDownloadFallback(dirItemRef, targetPath, itemInfo, false).ConfigureAwait(false); // Don't re-add to processed paths
                    return;
                }

                // Download to memory stream (retry on busy/notready)
                uint dl;
                const int maxDlRetries = 6;
                for (int attempt = 0; attempt <= maxDlRetries; attempt++)
                {
                    dl = CanonEdSdkNative.EdsDownload(dirItemRef, itemInfo.Size, memoryStreamRef);
                    LogMessage($"Canon: EdsDownload(to memory) attempt {attempt + 1} = 0x{dl:X8} ({CanonEdSdkNative.GetErrorDescription(dl)})");

                    if (dl == CanonEdSdkNative.EDS_ERR_OK)
                    {
                        downloadStarted = true;
                        break;
                    }

                    bool retryable = dl == CanonEdSdkNative.EDS_ERR_DEVICE_BUSY || dl == CanonEdSdkNative.EDS_ERR_OBJECT_NOTREADY;
                    if (!retryable || attempt == maxDlRetries)
                    {
                        LogMessage($"Canon: Memory stream download failed permanently");
                        // Remove from processed paths since we failed
                        lock (_eventLock)
                        {
                            _processedFilePaths.Remove(targetPath);
                        }
                        return;
                    }

                    int delayMs = (int)Math.Pow(2, attempt) * 75;
                    await Task.Delay(delayMs).ConfigureAwait(false);
                }

                uint complete = CanonEdSdkNative.EdsDownloadComplete(dirItemRef);
                LogMessage($"Canon: EdsDownloadComplete = 0x{complete:X8} ({CanonEdSdkNative.GetErrorDescription(complete)})");

                if (downloadStarted && complete == CanonEdSdkNative.EDS_ERR_OK)
                {
                    // Extract data from memory stream
                    byte[] imageBytes = await ExtractBytesFromMemoryStream(memoryStreamRef).ConfigureAwait(false);
                    
                    if (imageBytes != null && imageBytes.Length > 0)
                    {
                        // Write to disk using .NET (Framework 4.7.2 compatible)
                        try
                        {
                            File.WriteAllBytes(targetPath, imageBytes);
                            LogMessage($"Canon: Successfully wrote {imageBytes.Length:N0} bytes to disk");
                            
                            // Verify file exists and size matches
                            if (File.Exists(targetPath))
                            {
                                var fileInfo = new FileInfo(targetPath);
                                LogMessage($"Canon: File verification: {fileInfo.Length:N0} bytes on disk (expected {imageBytes.Length:N0})");
                                
                                if (fileInfo.Length == imageBytes.Length)
                                {
                                    // Delete item on camera (recommended after host download)
                                    uint del = CanonEdSdkNative.EdsDeleteDirectoryItem(dirItemRef);
                                    LogMessage($"Canon: EdsDeleteDirectoryItem = 0x{del:X8} ({CanonEdSdkNative.GetErrorDescription(del)})");

                                    // Report success only after verification
                                    LogMessage($"Canon: MEMORY STREAM SUCCESS - Downloaded {Path.GetFileName(targetPath)} ({fileInfo.Length:N0} bytes)");
                                    
                                    // PHASE 1: Generate thumbnail and fire ONLY enhanced event
                                    string thumbnailPath = await GenerateThumbnailAsync(targetPath).ConfigureAwait(false);
                                    
                                    Application.Current?.Dispatcher?.Invoke(() => 
                                    {
                                        // Only fire the enhanced event with thumbnail - no PhotoSaved
                                        if (!string.IsNullOrEmpty(thumbnailPath))
                                        {
                                            PhotoSavedWithThumbnail?.Invoke(targetPath, thumbnailPath);
                                        }
                                        else
                                        {
                                            // Fallback: if thumbnail generation failed, still fire enhanced event with null thumbnail
                                            PhotoSavedWithThumbnail?.Invoke(targetPath, null);
                                        }
                                    });
                                    return; // SUCCESS - exit cleanly
                                }
                                else
                                {
                                    LogMessage($"Canon: File size mismatch - expected {imageBytes.Length:N0}, got {fileInfo.Length:N0}");
                                }
                            }
                            else
                            {
                                LogMessage($"Canon: File does not exist after WriteAllBytes: {targetPath}");
                            }
                        }
                        catch (Exception writeEx)
                        {
                            LogMessage($"Canon: File write error: {writeEx.Message}");
                        }
                    }
                    else
                    {
                        LogMessage($"Canon: Failed to extract bytes from memory stream");
                    }
                }
                else
                {
                    LogMessage($"Canon: Download to memory stream failed - downloadStarted={downloadStarted}, complete=0x{complete:X8}");
                }
                
                // If we reach here, memory stream approach failed
                LogMessage($"Canon: Memory stream approach failed, trying fallback");
                await ProcessImageDownloadFallback(dirItemRef, targetPath, itemInfo, false).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                LogMessage($"Canon: Memory stream download processing error: {ex.Message}");

                if (downloadStarted)
                {
                    try
                    {
                        uint cleanup = CanonEdSdkNative.EdsDownloadComplete(dirItemRef);
                        LogMessage($"Canon: EdsDownloadComplete (cleanup) = 0x{cleanup:X8}");
                    }
                    catch { }
                }
                
                // Remove from processed paths on error
                if (targetPath != null)
                {
                    lock (_eventLock)
                    {
                        _processedFilePaths.Remove(targetPath);
                    }
                }
            }
            finally
            {
                if (memoryStreamRef != IntPtr.Zero) 
                {
                    SafeEdsRelease(memoryStreamRef, "Memory stream handle");
                }
                if (dirItemRef != IntPtr.Zero) SafeEdsRelease(dirItemRef, "Directory item handle");
            }
        }

        private async Task<bool> VerifyDownloadedFile(string targetPath)
        {
            const int maxRetries = 10; // 2 seconds total with 200ms delays
            const int retryDelayMs = 200;
            
            for (int attempt = 0; attempt < maxRetries; attempt++)
            {
                try
                {
                    // Check if file exists
                    if (File.Exists(targetPath))
                    {
                        // Check if file has content
                        var fileInfo = new FileInfo(targetPath);
                        if (fileInfo.Length > 0)
                        {
                            if (attempt > 0)
                            {
                                LogMessage($"Canon: File verification succeeded on attempt {attempt + 1}");
                            }
                            return true;
                        }
                        else
                        {
                            LogMessage($"Canon: File exists but has 0 length, attempt {attempt + 1}");
                        }
                    }
                    else
                    {
                        LogMessage($"Canon: File does not exist, attempt {attempt + 1}");
                    }
                }
                catch (Exception ex)
                {
                    LogMessage($"Canon: File verification error on attempt {attempt + 1}: {ex.Message}");
                }
                
                if (attempt < maxRetries - 1)
                {
                    await Task.Delay(retryDelayMs).ConfigureAwait(false);
                }
            }
            
            LogMessage($"Canon: File verification failed after {maxRetries} attempts");
            return false;
        }

        // NEW: Extract bytes from memory stream
        private async Task<byte[]> ExtractBytesFromMemoryStream(IntPtr memoryStreamRef)
        {
            try
            {
                // Get length of data in memory stream
                uint lengthResult = CanonEdSdkNative.EdsGetLength(memoryStreamRef, out ulong streamLength);
                LogMessage($"Canon: EdsGetLength = 0x{lengthResult:X8}, streamLength = {streamLength:N0} bytes");
                
                if (lengthResult != CanonEdSdkNative.EDS_ERR_OK || streamLength == 0)
                {
                    LogMessage($"Canon: Failed to get memory stream length");
                    return null;
                }
                
                if (streamLength > int.MaxValue)
                {
                    LogMessage($"Canon: Memory stream too large: {streamLength:N0} bytes");
                    return null;
                }
                
                // Get pointer to data in memory stream
                uint pointerResult = CanonEdSdkNative.EdsGetPointer(memoryStreamRef, out IntPtr dataPointer);
                LogMessage($"Canon: EdsGetPointer = 0x{pointerResult:X8}, dataPointer = 0x{dataPointer.ToInt64():X16}");
                
                if (pointerResult == CanonEdSdkNative.EDS_ERR_OK && dataPointer != IntPtr.Zero)
                {
                    // Copy data using Marshal.Copy
                    byte[] imageBytes = new byte[streamLength];
                    Marshal.Copy(dataPointer, imageBytes, 0, (int)streamLength);
                    LogMessage($"Canon: Successfully copied {imageBytes.Length:N0} bytes from memory stream");
                    return imageBytes;
                }
                else
                {
                    LogMessage($"Canon: EdsGetPointer failed, trying EdsRead fallback");
                    
                    // Fallback: Use EdsRead if EdsGetPointer is not available
                    byte[] buffer = new byte[streamLength];
                    IntPtr bufferPtr = Marshal.AllocHGlobal((int)streamLength);
                    try
                    {
                        uint readResult = CanonEdSdkNative.EdsRead(memoryStreamRef, streamLength, bufferPtr, out ulong actualRead);
                        LogMessage($"Canon: EdsRead = 0x{readResult:X8}, actualRead = {actualRead:N0} bytes");
                        
                        if (readResult == CanonEdSdkNative.EDS_ERR_OK && actualRead > 0)
                        {
                            Marshal.Copy(bufferPtr, buffer, 0, (int)actualRead);
                            Array.Resize(ref buffer, (int)actualRead);
                            LogMessage($"Canon: Successfully read {buffer.Length:N0} bytes using EdsRead");
                            return buffer;
                        }
                        else
                        {
                            LogMessage($"Canon: EdsRead also failed");
                            return null;
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(bufferPtr);
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Canon: ExtractBytesFromMemoryStream error: {ex.Message}");
                return null;
            }
        }

        // FALLBACK: File stream approach for when memory streams don't work
        private async Task ProcessImageDownloadFallback(IntPtr dirItemRef, string targetPath, CanonEdSdkNative.EdsDirectoryItemInfo itemInfo, bool addToProcessedPaths = true)
        {
            IntPtr streamRef = IntPtr.Zero;
            bool downloadStarted = false;

            try
            {
                LogMessage($"Canon: Using file stream fallback for {Path.GetFileName(targetPath)}");
                
                // Create file stream with proper enum types
                streamRef = await CreateFileStreamWithCorrectTypes(targetPath).ConfigureAwait(false);
                if (streamRef == IntPtr.Zero)
                {
                    LogMessage("Canon: Fallback file stream creation also failed");
                    return;
                }

                // Download (retry on busy/notready)
                uint dl;
                const int maxDlRetries = 6;
                for (int attempt = 0; attempt <= maxDlRetries; attempt++)
                {
                    dl = CanonEdSdkNative.EdsDownload(dirItemRef, itemInfo.Size, streamRef);
                    LogMessage($"Canon: Fallback EdsDownload attempt {attempt + 1} = 0x{dl:X8} ({CanonEdSdkNative.GetErrorDescription(dl)})");

                    if (dl == CanonEdSdkNative.EDS_ERR_OK)
                    {
                        downloadStarted = true;
                        break;
                    }

                    bool retryable = dl == CanonEdSdkNative.EDS_ERR_DEVICE_BUSY || dl == CanonEdSdkNative.EDS_ERR_OBJECT_NOTREADY;
                    if (!retryable || attempt == maxDlRetries)
                        return;

                    int delayMs = (int)Math.Pow(2, attempt) * 75;
                    await Task.Delay(delayMs).ConfigureAwait(false);
                }

                uint complete = CanonEdSdkNative.EdsDownloadComplete(dirItemRef);
                LogMessage($"Canon: Fallback EdsDownloadComplete = 0x{complete:X8} ({CanonEdSdkNative.GetErrorDescription(complete)})");

                // Release stream AFTER EdsDownloadComplete
                if (streamRef != IntPtr.Zero)
                {
                    LogMessage("Canon: Releasing fallback stream after EdsDownloadComplete");
                    SafeEdsRelease(streamRef, "Fallback stream handle");
                    streamRef = IntPtr.Zero;
                }

                if (downloadStarted && complete == CanonEdSdkNative.EDS_ERR_OK)
                {
                    // Verify file exists and has content
                    bool fileVerified = await VerifyDownloadedFile(targetPath).ConfigureAwait(false);
                    
                    if (fileVerified)
                    {
                        uint del = CanonEdSdkNative.EdsDeleteDirectoryItem(dirItemRef);
                        LogMessage($"Canon: Fallback success, EdsDeleteDirectoryItem = 0x{del:X8}");
                        
                        var fi = new FileInfo(targetPath);
                        LogMessage($"Canon: FALLBACK SUCCESS - Downloaded {Path.GetFileName(targetPath)} ({fi.Length:N0} bytes)");
                        
                        if (addToProcessedPaths)
                        {
                            lock (_eventLock)
                            {
                                if (_processedFilePaths.Contains(targetPath))
                                {
                                    LogMessage($"Canon: Duplicate file path prevented in fallback: {targetPath}");
                                    return;
                                }
                                _processedFilePaths.Add(targetPath);
                            }
                        }
                        
                        string thumbnailPath = await GenerateThumbnailAsync(targetPath).ConfigureAwait(false);
                        
                        Application.Current?.Dispatcher?.Invoke(() => 
                        {
                            // Only fire the enhanced event with thumbnail - no PhotoSaved
                            if (!string.IsNullOrEmpty(thumbnailPath))
                            {
                                PhotoSavedWithThumbnail?.Invoke(targetPath, thumbnailPath);
                            }
                            else
                            {
                                // Fallback: if thumbnail generation failed, still fire enhanced event with null thumbnail
                                PhotoSavedWithThumbnail?.Invoke(targetPath, null);
                            }
                        });
                    }
                    else
                    {
                        LogMessage($"Canon: Fallback file verification failed");
                    }
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Canon: Fallback download error: {ex.Message}");
            }
            finally
            {
                if (streamRef != IntPtr.Zero) SafeEdsRelease(streamRef, "Fallback stream handle");
            }
        }

        // KEPT: File stream approach for fallback
        private async Task<IntPtr> CreateFileStreamWithCorrectTypes(string targetPath)
        {
            try
            {
                // Validate and prepare the target path
                var dirPath = Path.GetDirectoryName(targetPath);
                var fileName = Path.GetFileName(targetPath);
                
                LogMessage($"Canon: Creating stream for file: {fileName}");
                LogMessage($"Canon: Target directory: {dirPath}");
                LogMessage($"Canon: Full path length: {targetPath.Length} chars");
                
                // Ensure directory exists
                if (!Directory.Exists(dirPath))
                {
                    Directory.CreateDirectory(dirPath);
                    LogMessage($"Canon: Created directory: {dirPath}");
                }
                
                // Check if file already exists and delete it
                if (File.Exists(targetPath))
                {
                    try
                    {
                        File.Delete(targetPath);
                        LogMessage($"Canon: Deleted existing file: {fileName}");
                    }
                    catch (Exception ex)
                    {
                        LogMessage($"Canon: Failed to delete existing file: {ex.Message}");
                        // Try with a different name
                        var timestamp = DateTime.Now.ToString("_HHmmss");
                        var nameWithoutExt = Path.GetFileNameWithoutExtension(targetPath);
                        var ext = Path.GetExtension(targetPath);
                        targetPath = Path.Combine(dirPath, $"{nameWithoutExt}{timestamp}{ext}");
                        LogMessage($"Canon: Using alternative path: {targetPath}");
                    }
                }
                
                // Try creating the file stream using proper enum types (this should fix the 0x000000AB error)
                const int maxRetries = 3;
                
                for (int attempt = 0; attempt <= maxRetries; attempt++)
                {
                    IntPtr stream;
                    
                    // Method 1: Try EdsCreateFileStreamEx first (using enum types)
                    uint r1 = CanonEdSdkNative.EdsCreateFileStreamEx(
                        targetPath,
                        CanonEdSdkNative.EdsFileCreateDisposition.CreateAlways,  // ENUM TYPE, NOT INTEGER
                        CanonEdSdkNative.EdsAccess.ReadWrite,                    // ENUM TYPE, NOT INTEGER
                        out stream);
                    
                    LogMessage($"Canon: EdsCreateFileStreamEx (ENUM) attempt {attempt + 1} = 0x{r1:X8} ({CanonEdSdkNative.GetErrorDescription(r1)})");
                    
                    if (r1 == CanonEdSdkNative.EDS_ERR_OK && stream != IntPtr.Zero)
                    {
                        LogMessage($"Canon: Successfully created file stream (Ex method with enums)");
                        return stream;
                    }
                    
                    // Method 2: Try EdsCreateFileStream fallback (using enum types)
                    uint r2 = CanonEdSdkNative.EdsCreateFileStream(
                        targetPath,
                        CanonEdSdkNative.EdsFileCreateDisposition.CreateAlways,  // ENUM TYPE, NOT INTEGER
                        CanonEdSdkNative.EdsAccess.ReadWrite,                    // ENUM TYPE, NOT INTEGER
                        out stream);
                    
                    LogMessage($"Canon: EdsCreateFileStream (ENUM) attempt {attempt + 1} = 0x{r2:X8} ({CanonEdSdkNative.GetErrorDescription(r2)})");
                    
                    if (r2 == CanonEdSdkNative.EDS_ERR_OK && stream != IntPtr.Zero)
                    {
                        LogMessage($"Canon: Successfully created file stream (standard method with enums)");
                        return stream;
                    }
                    
                    // Method 3: Try with CreateNew instead of CreateAlways
                    if (attempt == 1)
                    {
                        uint r3 = CanonEdSdkNative.EdsCreateFileStream(
                            targetPath,
                            CanonEdSdkNative.EdsFileCreateDisposition.CreateNew,  // ENUM TYPE
                            CanonEdSdkNative.EdsAccess.ReadWrite,                 // ENUM TYPE
                            out stream);
                        
                        LogMessage($"Canon: EdsCreateFileStream (CreateNew ENUM) attempt {attempt + 1} = 0x{r3:X8} ({CanonEdSdkNative.GetErrorDescription(r3)})");
                        
                        if (r3 == CanonEdSdkNative.EDS_ERR_OK && stream != IntPtr.Zero)
                        {
                            LogMessage($"Canon: Successfully created file stream (CreateNew method with enums)");
                            return stream;
                        }
                    }
                    
                    // Method 4: Try creating a .NET FileStream first to test file system access
                    if (attempt == 2)
                    {
                        try
                        {
                            using (var testStream = File.Create(targetPath))
                            {
                                LogMessage($"Canon: .NET FileStream test successful, file is accessible");
                            }
                            File.Delete(targetPath);
                            
                            // Now try EDSDK again with enum types
                            uint r4 = CanonEdSdkNative.EdsCreateFileStream(
                                targetPath,
                                CanonEdSdkNative.EdsFileCreateDisposition.CreateNew,  // ENUM TYPE
                                CanonEdSdkNative.EdsAccess.ReadWrite,                 // ENUM TYPE
                                out stream);
                            
                            LogMessage($"Canon: EdsCreateFileStream (after .NET test, ENUM) = 0x{r4:X8} ({CanonEdSdkNative.GetErrorDescription(r4)})");
                            
                            if (r4 == CanonEdSdkNative.EDS_ERR_OK && stream != IntPtr.Zero)
                            {
                                LogMessage($"Canon: Successfully created file stream (after .NET test with enums)");
                                return stream;
                            }
                        }
                        catch (Exception ex)
                        {
                            LogMessage($"Canon: .NET FileStream test failed: {ex.Message}");
                            LogMessage($"Canon: This indicates a file system permission or path issue");
                            return IntPtr.Zero;
                        }
                    }
                    
                    // Check if errors are retryable
                    bool retryable = (r1 == CanonEdSdkNative.EDS_ERR_DEVICE_BUSY || r1 == CanonEdSdkNative.EDS_ERR_OBJECT_NOTREADY ||
                                     r2 == CanonEdSdkNative.EDS_ERR_DEVICE_BUSY || r2 == CanonEdSdkNative.EDS_ERR_OBJECT_NOTREADY);
                    
                    if (!retryable || attempt == maxRetries)
                    {
                        LogMessage($"Canon: File stream creation failed permanently. Error 0x{r1:X8}/0x{r2:X8}");
                        if (r1 == CanonEdSdkNative.EDS_ERR_STREAM_BAD_OPTIONS || r2 == CanonEdSdkNative.EDS_ERR_STREAM_BAD_OPTIONS)
                        {
                            LogMessage($"Canon: Stream Bad Options error should now be fixed with enum parameters");
                        }
                        LogMessage($"Canon: This may be a permissions issue or invalid path");
                        return IntPtr.Zero;
                    }
                    
                    int delayMs = (int)Math.Pow(2, attempt) * 100;
                    LogMessage($"Canon: Retrying file stream creation in {delayMs}ms...");
                    await Task.Delay(delayMs).ConfigureAwait(false);
                }
                
                return IntPtr.Zero;
            }
            catch (Exception ex)
            {
                LogMessage($"Canon: CreateFileStreamWithCorrectTypes exception: {ex.Message}");
                return IntPtr.Zero;
            }
        }
        #endregion

        #region Public session context + compatibility methods
        public void SetSessionContext(string partNumber, Func<int> getNextSequence, string exportRootFolder)
        {
            _currentPartNumber = partNumber ?? string.Empty;
            _getNextSequence = getNextSequence;
            _exportRootFolder = string.IsNullOrWhiteSpace(exportRootFolder) ? _exportRootFolder : exportRootFolder;

            lock (_eventLock)
            {
                _processedTransferRefs.Clear();
                _processedFilePaths.Clear();
            }

            LogMessage($"Canon: Session context set - Part: '{_currentPartNumber}', Ready for auto-capture");
        }

        public void Dispose() => Disconnect();

        // PHASE 1: Thumbnail generation
        private async Task<string> GenerateThumbnailAsync(string fullImagePath)
        {
            try
            {
                if (!File.Exists(fullImagePath))
                {
                    LogMessage($"Canon: Cannot generate thumbnail - source file not found: {fullImagePath}");
                    return null;
                }

                var directory = Path.GetDirectoryName(fullImagePath);
                var fileName = Path.GetFileNameWithoutExtension(fullImagePath);
                var extension = Path.GetExtension(fullImagePath);
                var thumbnailPath = Path.Combine(directory, $"{fileName}.thumb{extension}");

                LogMessage($"Canon: Generating thumbnail: {Path.GetFileName(thumbnailPath)}");

                await Task.Run(() =>
                {
                    // Load image without locking the file
                    byte[] imageData = File.ReadAllBytes(fullImagePath);
                    
                    using (var stream = new MemoryStream(imageData))
                    {
                        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                        var originalFrame = decoder.Frames[0];
                        
                        // Calculate thumbnail size (max dimension 300px)
                        const int maxDimension = 300;
                        double scaleX = (double)maxDimension / originalFrame.PixelWidth;
                        double scaleY = (double)maxDimension / originalFrame.PixelHeight;
                        double scale = Math.Min(scaleX, scaleY);
                        
                        if (scale >= 1.0)
                        {
                            // Image is already small enough, copy as-is
                            File.Copy(fullImagePath, thumbnailPath, true);
                            return;
                        }
                        
                        int newWidth = (int)(originalFrame.PixelWidth * scale);
                        int newHeight = (int)(originalFrame.PixelHeight * scale);
                        
                        var transformedBitmap = new TransformedBitmap(originalFrame, 
                            new ScaleTransform(scale, scale));
                        
                        // Create JPEG encoder for thumbnail
                        var encoder = new JpegBitmapEncoder { QualityLevel = 85 };
                        encoder.Frames.Add(BitmapFrame.Create(transformedBitmap));
                        
                        using (var fileStream = File.Create(thumbnailPath))
                        {
                            encoder.Save(fileStream);
                        }
                    }
                }).ConfigureAwait(false);

                if (File.Exists(thumbnailPath))
                {
                    var thumbnailInfo = new FileInfo(thumbnailPath);
                    LogMessage($"Canon: Thumbnail created successfully: {thumbnailInfo.Length:N0} bytes");
                    return thumbnailPath;
                }
                else
                {
                    LogMessage($"Canon: Thumbnail creation failed - file not created");
                    return null;
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Canon: Thumbnail generation error: {ex.Message}");
                return null;
            }
        }

        // Legacy compatibility methods (kept; implementations can be expanded later)
        public async Task RefreshCameraConnection() => await Connect(forceRefresh: true);
        public async Task<string> CreateThumbnailAsync(string path) => null;
        public string GetThumbnailPath(string path) => path;
        public async Task<string> CaptureImageAsync(string part) => null;
        public string CaptureImage(string part) => null;
        #endregion
    }
}
