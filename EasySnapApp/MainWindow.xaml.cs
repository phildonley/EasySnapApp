using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using EasySnapApp.Models;
using EasySnapApp.Services;
using EasySnapApp.Views;
using EasySnapApp.Utils;
using Microsoft.Win32;
using Path = System.IO.Path;

namespace EasySnapApp
{
    public partial class MainWindow : Window
    {
        private readonly ScanSessionManager _sessionManager;
        private readonly ObservableCollection<ScanResult> _results;

        // Services (kept as-is so your project structure remains intact)
        private readonly CanonCameraService _cameraSvc;
        private readonly ThermalScannerService _thermalSvc;
        private readonly IntelIntellisenseService _intelSvc;
        private readonly LaserArrayService _laserSvc;
        private readonly ScaleService _scaleSvc;

        // Guard: prevent export settings events from firing during InitializeComponent / initial load
        private bool _isInitializingExportSettings = true;

        // Part-level data: one entry per part number, applied to all images for that part
        private class PartData
        {
            public double? WeightLb { get; set; }
            public double? LengthIn { get; set; }
            public double? WidthIn { get; set; }
            public double? HeightIn { get; set; }
            public string Notes { get; set; } = "";
        }

        private readonly Dictionary<string, PartData> _partData =
            new Dictionary<string, PartData>(StringComparer.OrdinalIgnoreCase);

        // Session log entries
        private readonly List<string> _sessionLogEntries = new List<string>();

        public MainWindow()
        {
            _isInitializingExportSettings = true;

            InitializeComponent();

            // Prepare exports folder
            var exports = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Exports");
            Directory.CreateDirectory(exports);

            // Instantiate services
            var barcode = new BarcodeScannerService();
            _scaleSvc = new ScaleService(testMode: false); // Real mode now

            _cameraSvc = new CanonCameraService(exports);
            _cameraSvc.Log += message => Dispatcher.Invoke(() => LogSessionMessage(message));
            _cameraSvc.PhotoSaved += OnCameraPhotoSaved;
            _cameraSvc.PhotoSavedWithThumbnail += OnCameraPhotoSavedWithThumbnail; // PHASE 1
            _thermalSvc = new ThermalScannerService();
            _intelSvc = new IntelIntellisenseService();
            _laserSvc = new LaserArrayService();

            // Session manager (kept)
            _sessionManager = new ScanSessionManager(barcode, _scaleSvc, _cameraSvc);
            _sessionManager.OnNewScanResult += SessionManager_OnNewScanResult;
            _sessionManager.OnStatusMessage += SessionManager_OnStatusMessage;

            // Bind results
            _results = new ObservableCollection<ScanResult>();
            SessionDataGrid.ItemsSource = _results;
            ThumbnailBar.ItemsSource = _results;

            StatusTextBlock.Text = "Ready.";
            LogSessionMessage("EasySnap started. Ready for part capture.");

            // Initialize devices drawer
            InitializeDevicesDrawer();

            // Load export settings without triggering SelectionChanged during initialization
            LoadExportSettings();
            LoadCameraSettings();

            _isInitializingExportSettings = false;
            
            // STEP 1: EDSDK Smoke Test - MUST PASS before any camera operations
            TestEdsdkOnStartup();
            
            // STEP 2: Connect to camera for real tethering
            InitializeCameraConnection();
        }

        private void SessionManager_OnStatusMessage(string message)
        {
            Dispatcher.Invoke(() =>
            {
                StatusTextBlock.Text = message;
                LogSessionMessage(message);
            });
        }

        private void LogSessionMessage(string message)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            var logEntry = $"[{timestamp}] {message}";
            _sessionLogEntries.Add(logEntry);

            // Update the log display
            SessionLogTextBox.Text = string.Join("\n", _sessionLogEntries);

            // Auto-scroll to bottom
            LogScrollViewer.ScrollToEnd();
            
            // ALSO SAVE TO FILE for easy copying
            try
            {
                var logFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "session_log.txt");
                File.AppendAllText(logFile, logEntry + "\n");
            }
            catch { /* Ignore file write errors */ }
        }

        #region Devices/Services Drawer

        private void InitializeDevicesDrawer()
        {
            RefreshComPorts();
            LoadScaleSettings();
            UpdateDeviceStatus();
            
            // Initial camera status update
            UpdateCameraStatus();
        }

        private void RefreshComPorts()
        {
            ComPortComboBox.Items.Clear();
            ComPortComboBox.Items.Add("(None)");

            try
            {
                var ports = System.IO.Ports.SerialPort.GetPortNames();
                foreach (var port in ports.OrderBy(p => p))
                {
                    ComPortComboBox.Items.Add(port);
                }

                LogSessionMessage($"Found {ports.Length} COM ports: {string.Join(", ", ports)}");
            }
            catch (Exception ex)
            {
                LogSessionMessage($"Failed to enumerate COM ports: {ex.Message}");
            }
        }

        private void LoadScaleSettings()
        {
            // Load saved COM port from user settings
            var savedPort = Properties.Settings.Default.ScaleComPort;
            if (!string.IsNullOrEmpty(savedPort))
            {
                // Try to select the saved port
                for (int i = 0; i < ComPortComboBox.Items.Count; i++)
                {
                    if (ComPortComboBox.Items[i].ToString() == savedPort)
                    {
                        ComPortComboBox.SelectedIndex = i;
                        _scaleSvc.PortName = savedPort;
                        LogSessionMessage($"Restored scale COM port: {savedPort}");
                        break;
                    }
                }
            }

            if (ComPortComboBox.SelectedIndex < 0)
            {
                ComPortComboBox.SelectedIndex = 0; // Select "(None)"
            }
        }

        private void SaveScaleSettings()
        {
            var selectedPort = ComPortComboBox.SelectedItem?.ToString();
            if (selectedPort != "(None)")
            {
                Properties.Settings.Default.ScaleComPort = selectedPort;
            }
            else
            {
                Properties.Settings.Default.ScaleComPort = "";
            }
            Properties.Settings.Default.Save();
        }

        private void UpdateDeviceStatus()
        {
            var cameraStatus = _cameraSvc.IsConnected ? $"Camera: {_cameraSvc.ConnectedModel}" : "Camera: Not connected";
            var scaleStatus = string.IsNullOrEmpty(_scaleSvc.PortName) 
                ? "Scale: Not configured" 
                : $"Scale: {_scaleSvc.PortName}";
            
            DeviceStatusSummary.Text = $"{cameraStatus}  |  {scaleStatus}";
            
            if (string.IsNullOrEmpty(_scaleSvc.PortName))
            {
                ScaleStatusTextBlock.Text = "Not configured";
                ScaleStatusTextBlock.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xFF, 0xAA, 0x66));
            }
            else
            {
                ScaleStatusTextBlock.Text = $"Ready: {_scaleSvc.PortName}";
                ScaleStatusTextBlock.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x66, 0xFF, 0x66));
            }
        }

        private void UpdateCameraStatus()
        {
            if (_cameraSvc.IsConnected)
            {
                CameraStatusTextBlock.Text = _cameraSvc.ConnectedModel;
                CameraStatusTextBlock.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x66, 0xFF, 0x66));
            }
            else
            {
                CameraStatusTextBlock.Text = "Not connected";
                CameraStatusTextBlock.Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0xFF, 0xAA, 0x66));
            }
            
            UpdateDeviceStatus();
        }

        #endregion
        
        #region PHASE 1: Image Loading and Session Management
        
        /// <summary>
        /// PHASE 1: File-safe image loading that doesn't lock files
        /// </summary>
        private BitmapImage LoadImageSafely(string imagePath)
        {
            try
            {
                if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
                    return null;
                
                // Load into memory to avoid file locking
                var imageData = File.ReadAllBytes(imagePath);
                
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad; // Critical: load into memory
                bitmap.StreamSource = new MemoryStream(imageData);
                bitmap.EndInit();
                bitmap.Freeze(); // Make it cross-thread safe
                
                return bitmap;
            }
            catch (Exception ex)
            {
                LogSessionMessage($"Failed to load image safely: {ex.Message}");
                return null;
            }
        }
        
        /// <summary>
        /// PHASE 1: Load existing session images from disk for current part
        /// </summary>
        public void LoadSessionImages(string partNumber)
        {
            try
            {
                if (string.IsNullOrEmpty(partNumber))
                    return;
                    
                var exports = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Exports");
                var partFolder = Path.Combine(exports, partNumber);
                
                if (!Directory.Exists(partFolder))
                {
                    LogSessionMessage($"Part folder not found: {partFolder}");
                    return;
                }
                
                LogSessionMessage($"Loading existing images for part: {partNumber}");
                
                // Find all image files for this part (skip thumbnails)
                var imageFiles = Directory.GetFiles(partFolder, $"{partNumber}.*.jpg")
                    .Where(f => !f.Contains(".thumb."))
                    .OrderByDescending(f => File.GetCreationTime(f)) // Newest first
                    .ToList();
                
                foreach (var imagePath in imageFiles)
                {
                    try
                    {
                        var result = new ScanResult
                        {
                            PartNumber = partNumber,
                            Sequence = GetSequenceFromFilename(imagePath),
                            ImageFileName = Path.GetFileName(imagePath),
                            TimeStamp = File.GetCreationTime(imagePath).ToString("yyyyMMdd_HHmmss"),
                            FullImagePath = imagePath
                        };
                        
                        // Look for corresponding thumbnail
                        var thumbPath = GetThumbnailPath(imagePath);
                        if (File.Exists(thumbPath))
                        {
                            result.ThumbnailPath = thumbPath;
                            result.ThumbnailImage = LoadImageSafely(thumbPath);
                        }
                        else
                        {
                            // No thumbnail exists, use full image (will be slow but functional)
                            result.ThumbnailImage = LoadImageSafely(imagePath);
                        }
                        
                        // Apply part-level data if available
                        if (_partData.TryGetValue(partNumber, out var partData))
                        {
                            result.LengthIn = partData.LengthIn ?? 0;
                            result.DepthIn = partData.WidthIn ?? 0;
                            result.HeightIn = partData.HeightIn ?? 0;
                            result.WeightLb = partData.WeightLb ?? 0;
                        }
                        
                        _results.Add(result);
                    }
                    catch (Exception ex)
                    {
                        LogSessionMessage($"Failed to load image {Path.GetFileName(imagePath)}: {ex.Message}");
                    }
                }
                
                LogSessionMessage($"Loaded {imageFiles.Count} existing images for part {partNumber}");
            }
            catch (Exception ex)
            {
                LogSessionMessage($"LoadSessionImages error: {ex.Message}");
            }
        }
        
        /// <summary>
        /// PHASE 1: Get thumbnail path for a given full image path
        /// </summary>
        private string GetThumbnailPath(string fullImagePath)
        {
            var directory = Path.GetDirectoryName(fullImagePath);
            var fileName = Path.GetFileNameWithoutExtension(fullImagePath);
            var extension = Path.GetExtension(fullImagePath);
            return Path.Combine(directory, $"{fileName}.thumb{extension}");
        }
        
        #endregion

        #region Camera Photo Event Handling
        
        /// <summary>
        /// PHASE 1: Enhanced photo saved handler with thumbnail support
        /// </summary>
        private void OnCameraPhotoSavedWithThumbnail(string fullImagePath, string thumbnailPath)
        {
            // Ensure this runs on UI thread
            Dispatcher.Invoke(() =>
            {
                try
                {
                    LogSessionMessage($"Photo saved with thumbnail: {fullImagePath}");
                    
                    // Extract part number from file path structure
                    var partNumber = ExtractPartNumberFromPath(fullImagePath);
                    if (string.IsNullOrEmpty(partNumber))
                    {
                        LogSessionMessage("Could not determine part number from photo path");
                        return;
                    }
                    
                    // Create ScanResult for the automatically captured image
                    var result = new ScanResult
                    {
                        PartNumber = partNumber,
                        Sequence = GetSequenceFromFilename(fullImagePath),
                        ImageFileName = Path.GetFileName(fullImagePath),
                        TimeStamp = DateTime.Now.ToString("yyyyMMdd_HHmmss"),
                        FullImagePath = fullImagePath,
                        ThumbnailPath = thumbnailPath
                    };
                    
                    // PHASE 1: Load thumbnail image with file-safe loading
                    result.ThumbnailImage = LoadImageSafely(thumbnailPath ?? fullImagePath);
                    
                    // Apply current part-level measurements if available
                    if (_partData.TryGetValue(partNumber, out var partData))
                    {
                        result.LengthIn = partData.LengthIn ?? 0;
                        result.DepthIn = partData.WidthIn ?? 0;  // Width -> DepthIn mapping
                        result.HeightIn = partData.HeightIn ?? 0;
                        result.WeightLb = partData.WeightLb ?? 0;
                    }
                    
                    // Add to collection (newest first by inserting at top)
                    _results.Insert(0, result);
                    
                    // Auto-select newest item and update preview
                    SessionDataGrid.SelectedItem = result;
                    SessionDataGrid.ScrollIntoView(result);
                    
                    // Update status
                    StatusTextBlock.Text = $"Auto-captured {result.ImageFileName} for {partNumber}";
                    LogSessionMessage($"AUTO-CAPTURE: Added to session - Sequence {result.Sequence} (with thumbnail)");
                }
                catch (Exception ex)
                {
                    LogSessionMessage($"Enhanced photo saved handler error: {ex.Message}");
                }
            });
        }
        
        /// <summary>
        /// STEP 4: Handle photo saved event from camera service
        /// </summary>
        private void OnCameraPhotoSaved(string localFilePath)
        {
            // Ensure this runs on UI thread
            Dispatcher.Invoke(() =>
            {
                try
                {
                    LogSessionMessage($"Photo saved event: {localFilePath}");
                    
                    // Extract part number from file path structure
                    var partNumber = ExtractPartNumberFromPath(localFilePath);
                    if (string.IsNullOrEmpty(partNumber))
                    {
                        LogSessionMessage("Could not determine part number from photo path");
                        return;
                    }
                    
                    // Create ScanResult for the automatically captured image
                    var result = new ScanResult
                    {
                        PartNumber = partNumber,
                        Sequence = GetSequenceFromFilename(localFilePath),
                        ImageFileName = Path.GetFileName(localFilePath),
                        TimeStamp = DateTime.Now.ToString("yyyyMMdd_HHmmss")
                    };
                    
                    // Apply current part-level measurements if available
                    if (_partData.TryGetValue(partNumber, out var partData))
                    {
                        result.LengthIn = partData.LengthIn ?? 0;
                        result.DepthIn = partData.WidthIn ?? 0;  // Width -> DepthIn mapping
                        result.HeightIn = partData.HeightIn ?? 0;
                        result.WeightLb = partData.WeightLb ?? 0;
                    }
                    
                    _results.Add(result);
                    
                    // Auto-select newest item and update preview
                    SessionDataGrid.SelectedItem = result;
                    SessionDataGrid.ScrollIntoView(result);
                    
                    // Update status
                    StatusTextBlock.Text = $"Auto-captured {result.ImageFileName} for {partNumber}";
                    LogSessionMessage($"AUTO-CAPTURE: Added to session - Sequence {result.Sequence}");
                    LogSessionMessage($"AUTO-CAPTURE: File saved at {localFilePath}");
                }
                catch (Exception ex)
                {
                    LogSessionMessage($"Photo saved handler error: {ex.Message}");
                }
            });
        }
        
        private string ExtractPartNumberFromPath(string filePath)
        {
            try
            {
                // Path should be: Exports\[PartNumber]\[PartNumber].[Sequence].jpg
                var fileName = Path.GetFileNameWithoutExtension(filePath);
                var parts = fileName.Split('.');
                if (parts.Length >= 1)
                {
                    return parts[0]; // Part number is before the first dot
                }
            }
            catch { }
            
            return null;
        }
        
        private int GetNextSequenceForPart(string partNumber)
        {
            // Find highest sequence number for this part and return next
            var existingSequences = _results
                .Where(r => string.Equals(r.PartNumber, partNumber, StringComparison.OrdinalIgnoreCase))
                .Select(r => r.Sequence)
                .ToList();
                
            if (existingSequences.Any())
            {
                return existingSequences.Max() + 2;
            }
            
            return 103; // Default starting sequence
        }
        
        private int GetSequenceFromFilename(string filePath)
        {
            try
            {
                var filename = Path.GetFileNameWithoutExtension(filePath);
                var parts = filename.Split('.');
                if (parts.Length >= 2 && int.TryParse(parts[1], out int seq))
                {
                    return seq;
                }
            }
            catch { }
            
            return 103; // Default sequence
        }
        
        #endregion

        #region EDSDK Smoke Test
        
        /// <summary>
        /// STEP 1: Test EDSDK loading on startup - MUST PASS before camera operations
        /// </summary>
        private void TestEdsdkOnStartup()
        {
            try
            {
                if (CanonSdkTest.TryInitialize(out string error))
                {
                    LogSessionMessage("EDSDK loaded successfully");
                }
                else
                {
                    LogSessionMessage($"EDSDK load failed: {error}");
                    LogSessionMessage("Camera capture will not be available until EDSDK issue is resolved");
                }
            }
            catch (Exception ex)
            {
                LogSessionMessage($"EDSDK test exception: {ex.Message}");
            }
        }
        
        /// <summary>
        /// STEP 2: Initialize camera connection for tethered capture
        /// </summary>
        private async void InitializeCameraConnection()
        {
            try
            {
                LogSessionMessage("Attempting to connect to Canon camera...");
                bool connected = await _cameraSvc.Connect(); // Startup connection, allow throttling
                
                if (connected)
                {
                    LogSessionMessage("STEP 2 SUCCESS: Camera connected and ready for shutter events");
                }
                else
                {
                    LogSessionMessage("STEP 2: Camera not connected - tethering not available");
                }
                
                UpdateCameraStatus();
            }
            catch (Exception ex)
            {
                LogSessionMessage($"Camera connection error: {ex.Message}");
            }
        }
        
        #endregion

        #region Camera Settings

        private void LoadCameraSettings()
        {
            // Camera settings will be implemented when Canon SDK is integrated
            var settings = Properties.Settings.Default;
            var format = settings.CameraImageFormat ?? "JPEG";
            LogSessionMessage($"Camera format preference: {format}");
        }

        private void CameraSettings_Changed(object sender, EventArgs e)
        {
            // Will be implemented when camera format UI is added
            LogSessionMessage("Camera settings changed");
        }

        #endregion

        #region Export Settings

        private void LoadExportSettings()
        {
            // During initial load, SelectionChanged can fire before all controls exist.
            // Keep it safe even if called early.
            if (DimUnitComboBox == null || WgtUnitComboBox == null || SiteIdTextBox == null)
                return;

            var settings = Properties.Settings.Default;

            // Temporarily suppress event logic while we set selections/text
            var prevInit = _isInitializingExportSettings;
            _isInitializingExportSettings = true;

            try
            {
                // Set dim unit
                DimUnitComboBox.SelectedIndex = settings.ExportDimUnit == "cm" ? 1 : 0;

                // Set weight unit
                WgtUnitComboBox.SelectedIndex = settings.ExportWgtUnit == "kg" ? 1 : 0;

                // Set site ID
                SiteIdTextBox.Text = settings.ExportSiteId ?? "733";
            }
            finally
            {
                _isInitializingExportSettings = prevInit;
            }
        }

        private void ExportSettings_Changed(object sender, EventArgs e)
        {
            // Avoid crashes during startup / XAML construction
            if (_isInitializingExportSettings)
                return;

            if (DimUnitComboBox == null || WgtUnitComboBox == null || SiteIdTextBox == null)
                return;

            var settings = Properties.Settings.Default;

            // Update dim unit
            var dimItem = DimUnitComboBox.SelectedItem as ComboBoxItem;
            settings.ExportDimUnit = (dimItem?.Content?.ToString() ?? "in").Trim();

            // Update weight unit
            var wgtItem = WgtUnitComboBox.SelectedItem as ComboBoxItem;
            settings.ExportWgtUnit = (wgtItem?.Content?.ToString() ?? "lb").Trim();

            // Update site ID
            settings.ExportSiteId = (SiteIdTextBox.Text ?? "733").Trim();

            // Set volume unit to match dimension unit per spec
            settings.ExportVolUnit = settings.ExportDimUnit;

            settings.Save();

            LogSessionMessage($"Export settings: {settings.ExportDimUnit}/{settings.ExportWgtUnit}, Site: {settings.ExportSiteId}");
        }

        #endregion

        #region Device Event Handlers

        private void RefreshPortsButton_Click(object sender, RoutedEventArgs e)
        {
            var previousSelection = ComPortComboBox.SelectedItem?.ToString();
            RefreshComPorts();

            // Try to restore previous selection
            if (!string.IsNullOrEmpty(previousSelection) && previousSelection != "(None)")
            {
                for (int i = 0; i < ComPortComboBox.Items.Count; i++)
                {
                    if (ComPortComboBox.Items[i].ToString() == previousSelection)
                    {
                        ComPortComboBox.SelectedIndex = i;
                        break;
                    }
                }
            }

            if (ComPortComboBox.SelectedIndex < 0)
            {
                ComPortComboBox.SelectedIndex = 0;
            }
        }

        private void ComPortComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selectedPort = ComPortComboBox.SelectedItem?.ToString();
            if (selectedPort == "(None)" || string.IsNullOrEmpty(selectedPort))
            {
                _scaleSvc.PortName = "";
            }
            else
            {
                _scaleSvc.PortName = selectedPort;
                LogSessionMessage($"Scale COM port set to: {selectedPort}");
            }

            SaveScaleSettings();
            UpdateDeviceStatus();
        }

        private void TestScaleButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_scaleSvc.PortName))
            {
                LogSessionMessage("Scale test: No COM port selected");
                return;
            }
            
            try
            {
                LogSessionMessage($"Testing scale on {_scaleSvc.PortName}...");
                var weight = _scaleSvc.CaptureWeightLbOnce();
                LogSessionMessage($"Scale test successful: {weight:F2} lb (Raw: '{_scaleSvc.LastRawLine}')");
                StatusTextBlock.Text = $"Scale test: {weight:F2} lb";
            }
            catch (Exception ex)
            {
                LogSessionMessage($"Scale test failed: {ex.Message}");
                if (!string.IsNullOrEmpty(_scaleSvc.LastRawLine))
                {
                    LogSessionMessage($"Raw scale output: '{_scaleSvc.LastRawLine}'");
                }
                StatusTextBlock.Text = "Scale test failed";
            }
        }

        private async void RefreshCameraButton_Click(object sender, RoutedEventArgs e)
        {
            LogSessionMessage("Refreshing camera connection...");
            bool connected = await _cameraSvc.Connect(forceRefresh: true); // User-initiated, bypass throttle
            UpdateCameraStatus();
            
            if (connected)
            {
                LogSessionMessage("Camera refresh successful");
            }
            else
            {
                LogSessionMessage("Camera refresh failed");
            }
        }

        #endregion

        public void TestDryRun()
        {
            Console.WriteLine("Dry run test success.");
        }

        private void NewButton_Click(object sender, RoutedEventArgs e)
        {
            LogSessionMessage("*** NewButton_Click called! ***");
            
            string part = PartNumberTextBox.Text?.Trim();
            LogSessionMessage($"Part number from text box: '{part}'");

            if (string.IsNullOrEmpty(part))
            {
                LogSessionMessage("Part number is empty - showing warning");
                MessageBox.Show("Please enter a Part Number.",
                                "Warning",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                return;
            }

            LogSessionMessage("Starting new session...");
            _sessionManager.StartNewSession(part);
            _results.Clear();
            
            // PHASE 1: Load existing images for this part
            LoadSessionImages(part);
            
            PreviewImage.Source = null;
            PreviewMetaTextBlock.Text = "";
            StatusTextBlock.Text = $"Started session for part {part}.";
            LogSessionMessage($"Session started for {part}");
            
            // CRITICAL: Set part number for automatic capture
            var exports = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Exports");
            LogSessionMessage($"DEBUG: Setting camera session context...");
            LogSessionMessage($"DEBUG: Part: '{part}', Exports: '{exports}'");
            
            try
            {
                _cameraSvc.SetSessionContext(part, () => GetNextSequenceForPart(part), exports);
                LogSessionMessage($"DEBUG: Camera session context set successfully");
                LogSessionMessage($"Camera ready for auto-capture when shutter pressed");
            }
            catch (Exception ex)
            {
                LogSessionMessage($"ERROR: SetSessionContext failed: {ex.Message}");
            }

            // Load existing part-level values into the left pane (if any)
            LoadPartDataIntoPane(part);
        }

        private void CaptureButton_Click(object sender, RoutedEventArgs e)
        {
            _sessionManager.Capture();
        }

        private void SessionManager_OnNewScanResult(ScanResult r)
        {
            Dispatcher.Invoke(() =>
            {
                // Apply part-level values to every image row for that part
                if (!string.IsNullOrWhiteSpace(r.PartNumber) && _partData.TryGetValue(r.PartNumber, out var pd))
                {
                    // NOTE: We intentionally map Width to DepthIn to match your existing model/binding
                    if (pd.LengthIn.HasValue) r.LengthIn = pd.LengthIn.Value;
                    if (pd.WidthIn.HasValue) r.DepthIn = pd.WidthIn.Value;
                    if (pd.HeightIn.HasValue) r.HeightIn = pd.HeightIn.Value;
                    if (pd.WeightLb.HasValue) r.WeightLb = pd.WeightLb.Value;
                }

                _results.Add(r);

                // Auto-select newest item and update preview
                SessionDataGrid.SelectedItem = r;
                SessionDataGrid.ScrollIntoView(r);
            });
        }

        private void SessionDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SessionDataGrid.SelectedItem is ScanResult sel)
            {
                // PHASE 1: Use FullImagePath if available, fallback to old logic
                string fullPath = sel.FullImagePath;
                if (string.IsNullOrEmpty(fullPath))
                {
                    // Fallback to old path construction for backwards compatibility
                    fullPath = Path.Combine(
                        AppDomain.CurrentDomain.BaseDirectory,
                        "Exports",
                        sel.ImageFileName);
                }

                if (File.Exists(fullPath))
                {
                    try
                    {
                        // Use file-safe loading for preview too
                        PreviewImage.Source = LoadImageSafely(fullPath);
                    }
                    catch
                    {
                        PreviewImage.Source = null;
                    }
                }
                else
                {
                    PreviewImage.Source = null;
                }

                // Update metadata text
                PreviewMetaTextBlock.Text =
                    $"Part: {sel.PartNumber}   Seq: {sel.Sequence}   File: {sel.ImageFileName}   Time: {sel.TimeStamp}";

                // Load the part-level values into the left pane (so you can edit quickly)
                if (!string.IsNullOrWhiteSpace(sel.PartNumber))
                    LoadPartDataIntoPane(sel.PartNumber);
            }
        }

        private void Thumbnail_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is System.Windows.Controls.Image img && img.DataContext is ScanResult result)
            {
                SessionDataGrid.SelectedItem = result;
                SessionDataGrid.ScrollIntoView(result);
            }
        }

        private void ApplyPartDataButton_Click(object sender, RoutedEventArgs e)
        {
            var part = PartNumberTextBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(part))
            {
                MessageBox.Show("Enter a Part Number first.",
                                "Warning",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                return;
            }

            if (!_partData.TryGetValue(part, out var pd))
            {
                pd = new PartData();
                _partData[part] = pd;
            }

            pd.WeightLb = TryParseDouble(WeightTextBox.Text);
            pd.LengthIn = TryParseDouble(LengthTextBox.Text);
            pd.WidthIn = TryParseDouble(WidthTextBox.Text);
            pd.HeightIn = TryParseDouble(HeightTextBox.Text);
            pd.Notes = NotesTextBox.Text ?? "";

            // Apply to all existing scan results for this part
            var updatedRows = 0;
            foreach (var row in _results.Where(r => string.Equals(r.PartNumber, part, StringComparison.OrdinalIgnoreCase)))
            {
                if (pd.LengthIn.HasValue) row.LengthIn = pd.LengthIn.Value;
                if (pd.WidthIn.HasValue) row.DepthIn = pd.WidthIn.Value; // Width -> DepthIn
                if (pd.HeightIn.HasValue) row.HeightIn = pd.HeightIn.Value;
                if (pd.WeightLb.HasValue) row.WeightLb = pd.WeightLb.Value;
                updatedRows++;
            }

            StatusTextBlock.Text = $"Applied dims/weight to part {part}.";
            var logDetails = new List<string>();
            if (pd.LengthIn.HasValue) logDetails.Add($"L={pd.LengthIn.Value:F2}\"");
            if (pd.WidthIn.HasValue) logDetails.Add($"W={pd.WidthIn.Value:F2}\"");
            if (pd.HeightIn.HasValue) logDetails.Add($"H={pd.HeightIn.Value:F2}\"");
            if (pd.WeightLb.HasValue) logDetails.Add($"Wt={pd.WeightLb.Value:F2}lb");
            LogSessionMessage($"Applied part measurements to {part} ({updatedRows} images): {string.Join(" ", logDetails)}");
        }

        private void CaptureWeightButton_Click(object sender, RoutedEventArgs e)
        {
            var part = PartNumberTextBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(part))
            {
                MessageBox.Show("Enter a Part Number first.",
                                "Warning",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                return;
            }

            // Try scale first if configured, fallback to manual
            double weightLb;
            bool scaleSuccess = false;

            if (!string.IsNullOrEmpty(_scaleSvc.PortName))
            {
                try
                {
                    LogSessionMessage($"Reading scale on {_scaleSvc.PortName}...");
                    weightLb = _scaleSvc.CaptureWeightLbOnce();
                    LogSessionMessage($"Scale read successful: {weightLb:F2} lb (Raw: '{_scaleSvc.LastRawLine}')");
                    scaleSuccess = true;

                    // Update the weight textbox with scale reading
                    WeightTextBox.Text = weightLb.ToString("F2");
                }
                catch (Exception ex)
                {
                    LogSessionMessage($"Scale read failed: {ex.Message}");
                    if (!string.IsNullOrEmpty(_scaleSvc.LastRawLine))
                    {
                        LogSessionMessage($"Raw scale output: '{_scaleSvc.LastRawLine}'");
                    }

                    // Fall back to manual entry
                    var manualWeight = TryParseDouble(WeightTextBox.Text);
                    if (manualWeight.HasValue)
                    {
                        weightLb = manualWeight.Value;
                        LogSessionMessage($"Using manual weight: {weightLb:F2} lb");
                    }
                    else
                    {
                        StatusTextBlock.Text = "Scale failed and no manual weight entered.";
                        LogSessionMessage("Scale failed and no manual weight in textbox");
                        return;
                    }
                }
            }
            else
            {
                // No scale configured - use manual entry
                var manualWeight = TryParseDouble(WeightTextBox.Text);
                if (manualWeight.HasValue)
                {
                    weightLb = manualWeight.Value;
                    LogSessionMessage($"Using manual weight: {weightLb:F2} lb (no scale configured)");
                }
                else
                {
                    StatusTextBlock.Text = "Enter a weight value first.";
                    LogSessionMessage("Capture Weight: No weight entered in textbox");
                    return;
                }
            }

            // Apply weight to part-level data
            if (!_partData.TryGetValue(part, out var pd))
            {
                pd = new PartData();
                _partData[part] = pd;
            }

            pd.WeightLb = weightLb;

            // Update all existing rows for this part
            foreach (var row in _results.Where(r => string.Equals(r.PartNumber, part, StringComparison.OrdinalIgnoreCase)))
            {
                row.WeightLb = weightLb;
            }

            var source = scaleSuccess ? "scale" : "manual";
            StatusTextBlock.Text = $"Applied weight {weightLb:F2} lb to part {part} ({source}).";
            LogSessionMessage($"Weight applied to {part}: {weightLb:F2} lb ({source})");
        }

        private void ExportCsvButton_Click(object sender, RoutedEventArgs e)
        {
            if (_results.Count == 0)
            {
                StatusTextBlock.Text = "No data to export.";
                return;
            }

            var sfd = new SaveFileDialog
            {
                Title = "Export CSV (one row per part number)",
                Filter = "CSV files (*.csv)|*.csv",
                FileName = $"{Properties.Settings.Default.ExportSiteId ?? "733"}_{DateTime.Now:yyyyMMdd}_{DateTime.Now:HHmmss}.csv"
            };

            if (sfd.ShowDialog() != true)
                return;

            try
            {
                // ONE ROW PER PART NUMBER with new specification
                CsvWriter.ExportOneRowPerPart(sfd.FileName, _results, LogSessionMessage);

                StatusTextBlock.Text = $"Exported CSV: {Path.GetFileName(sfd.FileName)}";
                LogSessionMessage($"Exported CSV: {Path.GetFileName(sfd.FileName)} ({_results.GroupBy(r => r.PartNumber, StringComparer.OrdinalIgnoreCase).Count()} parts, {_results.Count} images)");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Export failed:\n{ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusTextBlock.Text = "Export failed.";
                LogSessionMessage($"Export failed: {ex.Message}");
            }
        }

        private void LoadPartDataIntoPane(string part)
        {
            if (_partData.TryGetValue(part, out var pd))
            {
                WeightTextBox.Text = pd.WeightLb?.ToString() ?? "";
                LengthTextBox.Text = pd.LengthIn?.ToString() ?? "";
                WidthTextBox.Text = pd.WidthIn?.ToString() ?? "";
                HeightTextBox.Text = pd.HeightIn?.ToString() ?? "";
                NotesTextBox.Text = pd.Notes ?? "";
            }
            else
            {
                WeightTextBox.Text = "";
                LengthTextBox.Text = "";
                WidthTextBox.Text = "";
                HeightTextBox.Text = "";
                NotesTextBox.Text = "";
            }
        }

        private static double? TryParseDouble(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            if (double.TryParse(text.Trim(), out var v)) return v;
            return null;
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            // Dispose camera service properly
            _cameraSvc?.Dispose();
            Application.Current.Shutdown();
        }

        private void About_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("EasySnap v1.0\n© 2025 Phil",
                            "About",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
        }

        private void GeneralSettings_Click(object sender, RoutedEventArgs e)
        {
            StatusTextBlock.Text = "General Settings menu opened (not yet implemented)";
            // TODO: Open general settings window/dialog
        }

        private void PhotographyTools_Click(object sender, RoutedEventArgs e)
        {
            StatusTextBlock.Text = "Photography Tools menu opened (not yet implemented)";
            // TODO: Open photography tools window/dialog
        }

        private void OpenSettings_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new DeviceSettingsWindow(
                _cameraSvc,
                _thermalSvc,
                _intelSvc,
                _laserSvc)
            { Owner = this };

            dlg.ShowDialog();
        }
        
        #region Log Context Menu Handlers
        
        private void CopyAllLog_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var allText = string.Join(Environment.NewLine, _sessionLogEntries);
                Clipboard.SetText(allText);
                StatusTextBlock.Text = "All log entries copied to clipboard";
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"Failed to copy log: {ex.Message}";
            }
        }
        
        private void CopySelectedLog_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var selectedText = SessionLogTextBox.SelectedText;
                if (!string.IsNullOrEmpty(selectedText))
                {
                    Clipboard.SetText(selectedText);
                    StatusTextBlock.Text = "Selected log text copied to clipboard";
                }
                else
                {
                    StatusTextBlock.Text = "No text selected to copy";
                }
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"Failed to copy selected text: {ex.Message}";
            }
        }
        
        #endregion
    }
}
