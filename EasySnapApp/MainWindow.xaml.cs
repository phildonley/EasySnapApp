using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EasySnapApp.Models;
using EasySnapApp.Services;
using EasySnapApp.Views;
using EasySnapApp.Utils;
using EasySnapApp.Data; // PHASE 2: Database integration
using Microsoft.Win32;
using Path = System.IO.Path;

namespace EasySnapApp
{
    public partial class MainWindow : Window
    {
        private readonly ScanSessionManager _sessionManager;
        private readonly ObservableCollection<ScanResult> _results;
        
        // PHASE 3.9: Enhanced UI collections
        private readonly ObservableCollection<ImageRecordViewModel> _imageRecords;
        private bool _isUpdatingSelection = false; // Prevent infinite loops in selection sync

        // PHASE 2: Database persistence
        private readonly EasySnapDb _database;
        private readonly CaptureRepository _repository;
        private string _currentPartNumber;
        private CaptureSession _currentSession;

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

        // Undo functionality
        private readonly Stack<UndoAction> _undoStack = new Stack<UndoAction>();

        // ADD THIS BLOCK HERE:
        public enum UndoActionType
        {
            Delete
        }

        public class UndoAction
        {
            public string ActionId { get; set; }
            public DateTime Timestamp { get; set; }
            public UndoActionType ActionType { get; set; }
            public string Description { get; set; }
            public string RecycleFolderPath { get; set; }
            public List<DeletedImageInfo> DeletedImages { get; set; } = new List<DeletedImageInfo>();
        }

        public class DeletedImageInfo
        {
            public string OriginalFullPath { get; set; }
            public string OriginalThumbPath { get; set; }
            public string RecycledFullPath { get; set; }
            public string RecycledThumbPath { get; set; }
            public CapturedImage OriginalDbRecord { get; set; }
            public int OriginalIndex { get; set; }
        }

        public MainWindow()
        {
            _isInitializingExportSettings = true;

            InitializeComponent();

            // PHASE 2: Initialize database first
            try
            {
                _database = new EasySnapDb();
                _repository = new CaptureRepository(_database);
                _database.InitializeDatabase();
                LogSessionMessage($"DB initialized at {_database.DatabasePath}");
            }
            catch (Exception ex)
            {
                LogSessionMessage($"DB initialization failed: {ex.Message}");
                MessageBox.Show($"Database initialization failed: {ex.Message}\n\nThe application may not function correctly.", 
                               "Database Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                // Continue without database - app should still function in basic mode
            }

            // Prepare exports folder
            var exports = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Exports");
            Directory.CreateDirectory(exports);

            // Instantiate services
            var barcode = new BarcodeScannerService();
            _scaleSvc = new ScaleService(testMode: false); // Real mode now

            _cameraSvc = new CanonCameraService(exports);
            _cameraSvc.Log += message => Dispatcher.Invoke(() => LogSessionMessage(message));
            _cameraSvc.PhotoSaved += OnCameraPhotoSaved;
            _cameraSvc.PhotoSavedWithThumbnail += OnCameraPhotoSavedEnhanced; // PHASE 1
            _thermalSvc = new ThermalScannerService();
            _intelSvc = new IntelIntellisenseService();
            _laserSvc = new LaserArrayService();

            // Session manager (kept)
            _sessionManager = new ScanSessionManager(barcode, _scaleSvc, _cameraSvc);
            _sessionManager.OnNewScanResult += SessionManager_OnNewScanResult;
            _sessionManager.OnStatusMessage += SessionManager_OnStatusMessage;

            // Bind results
            _results = new ObservableCollection<ScanResult>();
            
            // PHASE 3.9: Initialize enhanced collections
            _imageRecords = new ObservableCollection<ImageRecordViewModel>();
            SessionDataGrid.ItemsSource = _imageRecords;
            ThumbnailBar.ItemsSource = _imageRecords;

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
            
            // PHASE 3.9: Load all captures from database (persistent gallery)
            LoadAllCapturesFromDatabase();
            
            // PHASE 3: Initialize export features (NO MORE SAMPLE DATA)
            InitializePhase3Features();
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
        
        #region PHASE 2: Database Session Loading
        
        /// <summary>
        /// PHASE 3.9: Load ALL captures from database on startup (persistent gallery)
        /// </summary>
        private void LoadAllCapturesFromDatabase()
        {
            try
            {
                if (_repository == null)
                {
                    LogSessionMessage("DB not available - skipping capture load");
                    return;
                }
                
                // Load ALL captures across all parts, newest first
                var allImages = _repository.GetAllImagesNewestFirst();
                LogSessionMessage($"Loading {allImages.Count} captures from database (all parts)");
                
                _imageRecords.Clear();

                foreach (var dbImage in allImages)
                {
                    try
                    {
                        var viewModel = ImageRecordViewModel.FromCapturedImage(dbImage);

                        // POPULATE _partData WITH DIMENSIONS FROM DATABASE
                        if (!_partData.ContainsKey(dbImage.PartNumber))
                        {
                            _partData[dbImage.PartNumber] = new PartData();
                        }

                        if (dbImage.WeightGrams.HasValue || dbImage.DimX.HasValue ||
                            dbImage.DimY.HasValue || dbImage.DimZ.HasValue)
                        {
                            var partData = _partData[dbImage.PartNumber];
                            partData.WeightLb = dbImage.WeightGrams / 453.592; // Convert grams to lb
                            partData.LengthIn = dbImage.DimX / 25.4; // Convert mm to inches
                            partData.WidthIn = dbImage.DimY / 25.4; // Convert mm to inches
                            partData.HeightIn = dbImage.DimZ / 25.4; // Convert mm to inches
                        }

                        // Load thumbnail image (non-blocking)
                        LoadThumbnailForViewModel(viewModel);

                        _imageRecords.Add(viewModel);
                    }
                    catch (Exception ex)
                    {
                        LogSessionMessage($"Failed to load image {Path.GetFileName(dbImage.FullPath)}: {ex.Message}");
                    }
                }

                // Restore last part number to text box for continued work
                var lastPartNumber = Properties.Settings.Default.LastPartNumber;
                if (!string.IsNullOrEmpty(lastPartNumber))
                {
                    PartNumberTextBox.Text = lastPartNumber;
                    _currentPartNumber = lastPartNumber;
                    
                    // Set up camera context for the last part
                    var exports = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Exports");
                    _cameraSvc.SetSessionContext(lastPartNumber, () => GetNextSequenceForPart(lastPartNumber), exports);
                }
                
                // Auto-select the newest item if any exist
                if (_imageRecords.Count > 0)
                {
                    _imageRecords[0].IsSelected = true;
                    SessionDataGrid.SelectedItem = _imageRecords[0];
                    SessionDataGrid.ScrollIntoView(_imageRecords[0]);
                }
                
                LogSessionMessage($"Loaded {_imageRecords.Count} captures across {_imageRecords.GroupBy(x => x.PartNumber).Count()} parts");
            }
            catch (Exception ex)
            {
                LogSessionMessage($"LoadAllCapturesFromDatabase error: {ex.Message}");
            }
        }
        
        /// <summary>
        /// PHASE 2: Load images from database for a part number
        /// </summary>
        private void LoadImagesFromDatabase(string partNumber)
        {
            try
            {
                if (_repository == null || string.IsNullOrEmpty(partNumber))
                    return;
                    
                var dbImages = _repository.GetImagesForPart(partNumber);
                LogSessionMessage($"Loaded {dbImages.Count} images from DB for part {partNumber}");
                
                foreach (var dbImage in dbImages)
                {
                    try
                    {
                        // Create ScanResult from database record
                        var result = new ScanResult
                        {
                            PartNumber = dbImage.PartNumber,
                            Sequence = dbImage.Sequence,
                            ImageFileName = Path.GetFileName(dbImage.FullPath),
                            TimeStamp = dbImage.CaptureTimeUtc.ToString("yyyyMMdd_HHmmss"),
                            FullImagePath = dbImage.FullPath,
                            ThumbnailPath = dbImage.ThumbPath
                        };
                        
                        // Set dimensions and weight if available
                        if (dbImage.WidthPx.HasValue) result.LengthIn = dbImage.WidthPx.Value / 100.0; // Convert from px (placeholder)
                        if (dbImage.HeightPx.HasValue) result.HeightIn = dbImage.HeightPx.Value / 100.0;
                        if (dbImage.WeightGrams.HasValue) result.WeightLb = dbImage.WeightGrams.Value * 0.00220462; // Convert g to lb
                        if (dbImage.DimX.HasValue) result.LengthIn = dbImage.DimX.Value;
                        if (dbImage.DimY.HasValue) result.DepthIn = dbImage.DimY.Value;
                        if (dbImage.DimZ.HasValue) result.HeightIn = dbImage.DimZ.Value;
                        
                        // Load thumbnail image (non-blocking)
                        LoadThumbnailForResult(result);
                        
                        // Apply part-level data if available
                        if (_partData.TryGetValue(partNumber, out var partData))
                        {
                            result.LengthIn = partData.LengthIn ?? result.LengthIn;
                            result.DepthIn = partData.WidthIn ?? result.DepthIn;
                            result.HeightIn = partData.HeightIn ?? result.HeightIn;
                            result.WeightLb = partData.WeightLb ?? result.WeightLb;
                        }
                        
                        _results.Add(result);
                    }
                    catch (Exception ex)
                    {
                        LogSessionMessage($"Failed to load DB image {Path.GetFileName(dbImage.FullPath)}: {ex.Message}");
                    }
                }
                
                // Clean up missing files
                if (_results.Count > 0)
                {
                    var cleanedCount = _repository.CleanupMissingFiles(partNumber);
                    if (cleanedCount > 0)
                    {
                        LogSessionMessage($"Marked {cleanedCount} missing files as deleted in DB");
                    }
                }
            }
            catch (Exception ex)
            {
                LogSessionMessage($"LoadImagesFromDatabase error: {ex.Message}");
            }
        }
        
        /// <summary>
        /// PHASE 2: Load thumbnail for a result (non-blocking)
        /// </summary>
        private void LoadThumbnailForResult(ScanResult result)
        {
            try
            {
                // Try thumbnail first
                if (!string.IsNullOrEmpty(result.ThumbnailPath) && File.Exists(result.ThumbnailPath))
                {
                    result.ThumbnailImage = LoadImageSafely(result.ThumbnailPath);
                }
                // Fallback to full image (slower but functional)
                else if (!string.IsNullOrEmpty(result.FullImagePath) && File.Exists(result.FullImagePath))
                {
                    result.ThumbnailImage = LoadImageSafely(result.FullImagePath);
                }
                // If no file exists, use placeholder
                else
                {
                    // Could load a "missing file" placeholder here
                    result.ThumbnailImage = null;
                }
            }
            catch (Exception ex)
            {
                LogSessionMessage($"Failed to load thumbnail for {result.ImageFileName}: {ex.Message}");
                result.ThumbnailImage = null;
            }
        }
        
        /// <summary>
        /// PHASE 3.9: Load thumbnail for enhanced view model (non-blocking)
        /// </summary>
        private void LoadThumbnailForViewModel(ImageRecordViewModel viewModel)
        {
            try
            {
                // Try thumbnail first
                if (!string.IsNullOrEmpty(viewModel.ThumbPath) && File.Exists(viewModel.ThumbPath))
                {
                    viewModel.ThumbnailImage = LoadImageSafely(viewModel.ThumbPath);
                }
                // Fallback to full image (slower but functional)
                else if (!string.IsNullOrEmpty(viewModel.FullPath) && File.Exists(viewModel.FullPath))
                {
                    viewModel.ThumbnailImage = LoadImageSafely(viewModel.FullPath);
                }
                // If no file exists, use placeholder
                else
                {
                    // Could load a "missing file" placeholder here
                    viewModel.ThumbnailImage = null;
                }
            }
            catch (Exception ex)
            {
                LogSessionMessage($"Failed to load thumbnail for {viewModel.FileName}: {ex.Message}");
                viewModel.ThumbnailImage = null;
            }
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
                    
                    // PHASE 2: Save to database first
                    if (_repository != null)
                    {
                        try
                        {
                            var sequence = GetSequenceFromFilename(fullImagePath);
                            var fileInfo = new FileInfo(fullImagePath);
                            
                            var capturedImage = new CapturedImage
                            {
                                ImageId = Guid.NewGuid().ToString(),
                                SessionId = _currentSession?.SessionId ?? Guid.NewGuid().ToString(),
                                PartNumber = partNumber,
                                Sequence = sequence,
                                FullPath = fullImagePath,
                                ThumbPath = thumbnailPath,
                                CaptureTimeUtc = DateTime.UtcNow,
                                FileSizeBytes = fileInfo.Length,
                                IsDeleted = false
                            };
                            
                            _repository.InsertCapturedImage(capturedImage);
                            LogSessionMessage($"Inserted image row: {partNumber}.{sequence:000} ({fileInfo.Length:N0} bytes)");
                        }
                        catch (Exception dbEx)
                        {
                            LogSessionMessage($"DB insert failed: {dbEx.Message}");
                        }
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
            // PHASE 2: Use database if available
            if (_repository != null && !string.IsNullOrEmpty(partNumber))
            {
                try
                {
                    return _repository.GetNextSequenceForPart(partNumber);
                }
                catch (Exception ex)
                {
                    LogSessionMessage($"DB sequence lookup failed: {ex.Message}");
                }
            }
            
            // Fallback to in-memory calculation
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
            // DON'T clear results - keep previous parts visible
            // _results.Clear(); // REMOVED - keep previous work visible
            
            // PHASE 2: Database session management
            if (_repository != null)
            {
                try
                {
                    _currentSession = _repository.GetOrCreateActiveSession(part);
                    _currentPartNumber = part;
                    
                    // Save as last part number
                    Properties.Settings.Default.LastPartNumber = part;
                    Properties.Settings.Default.Save();
                    
                    LogSessionMessage($"DB session ready: {_currentSession.SessionId}");
                    
                    // Load existing images from database
                    LoadImagesFromDatabase(part);
                }
                catch (Exception dbEx)
                {
                    LogSessionMessage($"DB session setup failed: {dbEx.Message}");
                }
            }
            else
            {
                // Fallback to old file-based loading
                LoadSessionImages(part);
            }

            PreviewImage.Source = null;
            PreviewMetaTextBlock.Text = "";

            // Clear weight field for new part (but keep scale tared)
            WeightTextBox.Text = "";

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
            if (_isUpdatingSelection) return;
            
            _isUpdatingSelection = true;
            try
            {
                // Handle multiple selection sync
                var selectedViewModels = SessionDataGrid.SelectedItems.Cast<ImageRecordViewModel>().ToList();
                
                // Clear all thumbnail selections
                foreach (var item in _imageRecords)
                {
                    item.IsSelected = false;
                }
                
                // Set selected thumbnails
                foreach (var selectedItem in selectedViewModels)
                {
                    selectedItem.IsSelected = true;
                }
                
                // Update preview with first selected item
                var firstSelected = selectedViewModels.FirstOrDefault();
                if (firstSelected != null)
                {
                    UpdatePreviewImage(firstSelected);
                    
                    // Load the part-level values into the left pane
                    if (!string.IsNullOrWhiteSpace(firstSelected.PartNumber))
                        LoadPartDataIntoPane(firstSelected.PartNumber);
                }
            }
            finally
            {
                _isUpdatingSelection = false;
            }
            
            // Legacy support for ScanResult (single selection only)
            if (SessionDataGrid.SelectedItem is ScanResult scanResult)
            {
                // PHASE 1: Use FullImagePath if available, fallback to old logic
                string fullPath = scanResult.FullImagePath;
                if (string.IsNullOrEmpty(fullPath))
                {
                    // Fallback to old path construction for backwards compatibility
                    fullPath = Path.Combine(
                        AppDomain.CurrentDomain.BaseDirectory,
                        "Exports",
                        scanResult.ImageFileName);
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
                    $"Part: {scanResult.PartNumber}   Seq: {scanResult.Sequence}   File: {scanResult.ImageFileName}   Time: {scanResult.TimeStamp}";

                // Load the part-level values into the left pane (so you can edit quickly)
                if (!string.IsNullOrWhiteSpace(scanResult.PartNumber))
                    LoadPartDataIntoPane(scanResult.PartNumber);
            }
        }

        private void Thumbnail_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is System.Windows.Controls.Image img)
            {
                if (img.DataContext is ImageRecordViewModel viewModel)
                {
                    // Multi-select support
                    bool isCtrlPressed = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
                    bool isShiftPressed = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
                    
                    if (isCtrlPressed)
                    {
                        // Ctrl+Click: Toggle selection
                        viewModel.IsSelected = !viewModel.IsSelected;
                        
                        // Update DataGrid selection
                        _isUpdatingSelection = true;
                        try
                        {
                            if (viewModel.IsSelected)
                            {
                                if (!SessionDataGrid.SelectedItems.Contains(viewModel))
                                    SessionDataGrid.SelectedItems.Add(viewModel);
                            }
                            else
                            {
                                SessionDataGrid.SelectedItems.Remove(viewModel);
                            }
                        }
                        finally
                        {
                            _isUpdatingSelection = false;
                        }
                        
                        // Update preview to first selected item
                        var firstSelected = _imageRecords.FirstOrDefault(x => x.IsSelected);
                        if (firstSelected != null)
                            UpdatePreviewImage(firstSelected);
                    }
                    else if (isShiftPressed)
                    {
                        // Shift+Click: Range selection
                        var lastSelected = SessionDataGrid.SelectedItem as ImageRecordViewModel;
                        if (lastSelected != null)
                        {
                            var startIndex = _imageRecords.IndexOf(lastSelected);
                            var endIndex = _imageRecords.IndexOf(viewModel);
                            
                            if (startIndex >= 0 && endIndex >= 0)
                            {
                                var minIndex = Math.Min(startIndex, endIndex);
                                var maxIndex = Math.Max(startIndex, endIndex);
                                
                                _isUpdatingSelection = true;
                                try
                                {
                                    // Clear existing selections
                                    foreach (var item in _imageRecords)
                                        item.IsSelected = false;
                                    SessionDataGrid.SelectedItems.Clear();
                                    
                                    // Select range
                                    for (int i = minIndex; i <= maxIndex; i++)
                                    {
                                        _imageRecords[i].IsSelected = true;
                                        SessionDataGrid.SelectedItems.Add(_imageRecords[i]);
                                    }
                                }
                                finally
                                {
                                    _isUpdatingSelection = false;
                                }
                            }
                        }
                        else
                        {
                            // No previous selection, treat as single click
                            SyncSelection(viewModel);
                        }
                    }
                    else
                    {
                        // Normal click: Single selection
                        SyncSelection(viewModel);
                    }
                    
                    SessionDataGrid.ScrollIntoView(viewModel);
                }
                // Legacy support
                else if (img.DataContext is ScanResult result)
                {
                    SessionDataGrid.SelectedItem = result;
                    SessionDataGrid.ScrollIntoView(result);
                }
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

        /// <summary>
        /// + Button: Capture weight from scale or use manual entry
        /// </summary>
        private void WeightPlusButton_Click(object sender, RoutedEventArgs e)
        {
            var part = PartNumberTextBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(part))
            {
                MessageBox.Show("Enter a Part Number first.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            CaptureWeightForPart(part, showMessages: true);
        }

        /// <summary>
        /// - Button: Tare scale and clear weight field
        /// </summary>
        private void WeightMinusButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Tare the scale if configured
                if (!string.IsNullOrEmpty(_scaleSvc.PortName))
                {
                    LogSessionMessage($"Taring scale on {_scaleSvc.PortName}...");
                    _scaleSvc.TareScale();
                    LogSessionMessage($"Scale tared successfully: {_scaleSvc.LastRawLine}");
                }

                // Clear weight field
                WeightTextBox.Text = "";

                // Clear weight from current part data if exists
                var part = PartNumberTextBox.Text?.Trim();
                if (!string.IsNullOrWhiteSpace(part) && _partData.TryGetValue(part, out var partData))
                {
                    partData.WeightLb = null;
                    LogSessionMessage($"Cleared weight for part {part}");
                }

                StatusTextBlock.Text = "Scale tared and weight cleared.";
            }
            catch (Exception ex)
            {
                LogSessionMessage($"Tare failed: {ex.Message}");
                StatusTextBlock.Text = $"Tare failed: {ex.Message}";

                // Still clear the text field even if scale tare failed
                WeightTextBox.Text = "";
            }
        }

        /// <summary>
        /// Core weight capture logic - used by + button and auto-capture
        /// </summary>
        private double? CaptureWeightForPart(string partNumber, bool showMessages = false)
        {
            if (string.IsNullOrWhiteSpace(partNumber))
                return null;

            double weightLb;
            bool scaleSuccess = false;

            // Try scale first if configured, fallback to manual
            if (!string.IsNullOrEmpty(_scaleSvc.PortName))
            {
                try
                {
                    if (showMessages) LogSessionMessage($"Reading scale on {_scaleSvc.PortName}...");
                    weightLb = _scaleSvc.CaptureWeightLbOnce();
                    if (showMessages) LogSessionMessage($"Scale read successful: {weightLb:F2} lb (Raw: '{_scaleSvc.LastRawLine}')");
                    scaleSuccess = true;

                    // Update the weight textbox with scale reading
                    WeightTextBox.Text = weightLb.ToString("F2");
                }
                catch (Exception ex)
                {
                    if (showMessages)
                    {
                        LogSessionMessage($"Scale read failed: {ex.Message}");
                        if (!string.IsNullOrEmpty(_scaleSvc.LastRawLine))
                        {
                            LogSessionMessage($"Raw scale output: '{_scaleSvc.LastRawLine}'");
                        }
                    }

                    // Fall back to manual entry
                    var manualWeight = TryParseDouble(WeightTextBox.Text);
                    if (manualWeight.HasValue)
                    {
                        weightLb = manualWeight.Value;
                        if (showMessages) LogSessionMessage($"Using manual weight: {weightLb:F2} lb");
                    }
                    else
                    {
                        if (showMessages)
                        {
                            StatusTextBlock.Text = "Scale failed and no manual weight entered.";
                            LogSessionMessage("Scale failed and no manual weight in textbox");
                        }
                        return null;
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
                    if (showMessages) LogSessionMessage($"Using manual weight: {weightLb:F2} lb (no scale configured)");
                }
                else
                {
                    if (showMessages)
                    {
                        StatusTextBlock.Text = "Enter a weight value first.";
                        LogSessionMessage("No weight available - neither scale nor manual entry");
                    }
                    return null;
                }
            }

            // Apply weight to part-level data
            if (!_partData.TryGetValue(partNumber, out var pd))
            {
                pd = new PartData();
                _partData[partNumber] = pd;
            }

            pd.WeightLb = weightLb;

            // Update existing UI records for this part
            foreach (var record in _imageRecords.Where(r => string.Equals(r.PartNumber, partNumber, StringComparison.OrdinalIgnoreCase)))
            {
                record.WeightLb = weightLb;
            }

            if (showMessages)
            {
                var source = scaleSuccess ? "scale" : "manual";
                StatusTextBlock.Text = $"Applied weight {weightLb:F2} lb to part {partNumber} ({source}).";
                LogSessionMessage($"Weight applied to {partNumber}: {weightLb:F2} lb ({source})");
            }

            return weightLb;
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

        private void UndoMenuItem_Click(object sender, RoutedEventArgs e)
        {
            UndoLastDelete();
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
            try
            {
                var settingsWindow = new DimsExportSettingsWindow()
                {
                    Owner = this
                };
                settingsWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                LogSessionMessage($"Error opening DIMS settings: {ex.Message}");
                MessageBox.Show($"Error opening DIMS settings: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
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
        
        #region Phase 3: Export Functionality
        
        /// <summary>
        /// PHASE 3: Initialize export features without sample data
        /// </summary>
        private void InitializePhase3Features()
        {
            try
            {
                // PHASE 3: No more sample data creation - use real database only
                LogSessionMessage("Phase 3: Export features initialized (real data only)");
            }
            catch (Exception ex)
            {
                LogSessionMessage($"Phase 3 initialization warning: {ex.Message}");
            }
        }
        
        /// <summary>
        /// PHASE 3: Open the Export Window with real database path
        /// </summary>
        private void OpenExportWindow_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Pass the actual repository instance so ExportWindow uses the same database connection
                var exportWindow = new EasySnapApp.Views.ExportWindow(_repository)
                {
                    Owner = this
                };
                exportWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                LogSessionMessage($"Error opening Export window: {ex.Message}");
                MessageBox.Show($"Error opening Export window: {ex.Message}", "Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        #endregion
        
        #region Phase 3.9: Barcode UX Improvements
        
        /// <summary>
        /// Phase 3.9: Handle Enter key in part number textbox to trigger New session
        /// </summary>
        private void PartNumberTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                try
                {
                    // Trigger New button click safely
                    NewButton_Click(sender, new RoutedEventArgs());
                    e.Handled = true;
                }
                catch (Exception ex)
                {
                    LogSessionMessage($"Enter key handler error: {ex.Message}");
                }
            }
        }
        
        /// <summary>
        /// Phase 3.9: Auto-select all text when part number textbox receives focus
        /// </summary>
        private void PartNumberTextBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            try
            {
                if (sender is TextBox textBox)
                {
                    textBox.SelectAll();
                }
            }
            catch (Exception ex)
            {
                LogSessionMessage($"Focus handler error: {ex.Message}");
            }
        }
        
        #endregion
        
        #region Phase 3.9: Enhanced UI Features
        
        /// <summary>
        /// Phase 3.9: Enhanced selection sync between thumbnail and data grid
        /// </summary>
        private void SyncSelection(ImageRecordViewModel selectedItem)
        {
            if (_isUpdatingSelection || selectedItem == null) return;
            
            try
            {
                _isUpdatingSelection = true;
                
                // Clear all selections
                foreach (var item in _imageRecords)
                {
                    item.IsSelected = false;
                }
                
                // Set the selected item
                selectedItem.IsSelected = true;
                
                // Update both controls
                SessionDataGrid.SelectedItem = selectedItem;
                // Note: ThumbnailBar will update automatically via binding
                
                // Update preview image
                UpdatePreviewImage(selectedItem);
            }
            finally
            {
                _isUpdatingSelection = false;
            }
        }
        
        /// <summary>
        /// Phase 3.9: Update preview image for selected item
        /// </summary>
        private void UpdatePreviewImage(ImageRecordViewModel selectedItem)
        {
            if (selectedItem == null) return;
            
            string fullPath = selectedItem.FullPath;
            if (File.Exists(fullPath))
            {
                try
                {
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
                $"Part: {selectedItem.PartNumber}   Seq: {selectedItem.Sequence}   File: {selectedItem.FileName}   Time: {selectedItem.TimeStamp}";
        }
        
        /// <summary>
        /// Phase 3.9: Optional collapse parts setting (infrastructure only)
        /// </summary>
        private bool AutoCollapseParts
        {
            get { return Properties.Settings.Default.AutoCollapseParts; }
            set 
            { 
                Properties.Settings.Default.AutoCollapseParts = value;
                Properties.Settings.Default.Save();
                LogSessionMessage($"AutoCollapseParts setting: {value}");
            }
        }
        
        /// <summary>
        /// Phase 3.9: Enhanced photo saved handler with new view model
        /// </summary>
        private void OnCameraPhotoSavedEnhanced(string fullImagePath, string thumbnailPath)
        {
            Dispatcher.Invoke(() =>
            {
                try
                {
                    LogSessionMessage($"Photo saved with thumbnail: {fullImagePath}");
                    
                    var partNumber = ExtractPartNumberFromPath(fullImagePath);
                    if (string.IsNullOrEmpty(partNumber))
                    {
                        LogSessionMessage("Could not determine part number from photo path");
                        return;
                    }
                    
                    // Save to database first
                    if (_repository != null)
                    {
                        try
                        {
                            var sequence = GetSequenceFromFilename(fullImagePath);
                            var fileInfo = new FileInfo(fullImagePath);

                            var capturedImage = new CapturedImage
                            {
                                ImageId = Guid.NewGuid().ToString(),
                                SessionId = _currentSession?.SessionId ?? Guid.NewGuid().ToString(),
                                PartNumber = partNumber,
                                Sequence = sequence,
                                FullPath = fullImagePath,
                                ThumbPath = thumbnailPath,
                                CaptureTimeUtc = DateTime.UtcNow,
                                FileSizeBytes = fileInfo.Length,
                                IsDeleted = false
                            };

                            // COPY DIMENSIONS FROM _partData TO DATABASE RECORD
                            if (_partData.TryGetValue(partNumber, out var dbPartData))
                            {
                                capturedImage.WeightGrams = dbPartData.WeightLb * 453.592; // Convert lb to grams
                                capturedImage.DimX = dbPartData.LengthIn * 25.4; // Convert inches to mm
                                capturedImage.DimY = dbPartData.WidthIn * 25.4; // Convert inches to mm  
                                capturedImage.DimZ = dbPartData.HeightIn * 25.4; // Convert inches to mm
                            }

                            _repository.InsertCapturedImage(capturedImage);
                            
                            // Create view model and add to UI
                            var viewModel = ImageRecordViewModel.FromCapturedImage(capturedImage);
                            LoadThumbnailForViewModel(viewModel);

                            // AUTO-CAPTURE WEIGHT: If no weight exists for this part, try to capture it now
                            if (!_partData.ContainsKey(partNumber) || !_partData[partNumber].WeightLb.HasValue)
                            {
                                var autoWeight = CaptureWeightForPart(partNumber, showMessages: false);
                                if (autoWeight.HasValue)
                                {
                                    LogSessionMessage($"AUTO-CAPTURE: Weight {autoWeight:F2} lb captured with photo");
                                }
                            }

                            // Apply part-level measurements if available
                            if (_partData.TryGetValue(partNumber, out var partData))
                            {
                                viewModel.LengthIn = partData.LengthIn;
                                viewModel.DepthIn = partData.WidthIn;
                                viewModel.HeightIn = partData.HeightIn;
                                viewModel.WeightLb = partData.WeightLb;
                            }

                            // Insert at the top (newest first)
                            _imageRecords.Insert(0, viewModel);
                            
                            // Auto-select the new item
                            SyncSelection(viewModel);
                            
                            LogSessionMessage($"Added new capture: {viewModel.DisplayName}");
                        }
                        catch (Exception dbEx)
                        {
                            LogSessionMessage($"DB insert failed: {dbEx.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogSessionMessage($"Enhanced photo saved handler error: {ex.Message}");
                }
            });
        }
        
        /// <summary>
        /// Phase 3.9: Safe delete with confirmation - SOFT DELETE implementation
        /// </summary>
        private void DeleteSelectedCaptures()
        {
            // Step 1B: Delete audit logging
            var isSelectedCount = _imageRecords.Count(x => x.IsSelected);
            var dataGridSelectedCount = SessionDataGrid?.SelectedItems?.Count ?? 0;
            var focusedElementType = Keyboard.FocusedElement?.GetType()?.Name ?? "NULL";
            var currentPart = string.IsNullOrEmpty(_currentPartNumber) ? "NULL" : _currentPartNumber;
            
            LogSessionMessage($"DELETE AUDIT: IsSelected={isSelectedCount}, DataGrid.SelectedItems={dataGridSelectedCount}, FocusedElement={focusedElementType}, CurrentPart={currentPart}");
            
            // Step 1B: Selection reconciliation - treat DataGrid as canonical if it has items
            if (dataGridSelectedCount > 0)
            {
                LogSessionMessage($"DELETE AUDIT: DataGrid canonical reconciliation IsSelected={isSelectedCount} DataGrid={dataGridSelectedCount}");
                
                // Clear all IsSelected flags
                foreach (var item in _imageRecords)
                {
                    item.IsSelected = false;
                }
                
                // Set IsSelected=true for each DataGrid selected item
                foreach (var selectedItem in SessionDataGrid.SelectedItems.OfType<ImageRecordViewModel>())
                {
                    selectedItem.IsSelected = true;
                }
                
                var postSyncCount = _imageRecords.Count(x => x.IsSelected);
                LogSessionMessage($"DELETE AUDIT POST-SYNC: IsSelected={postSyncCount}");
            }
            
            var selectedItems = _imageRecords.Where(x => x.IsSelected).ToList();
            
            // Log final delete target counts before confirmation
            var finalIsSelectedCount = _imageRecords.Count(x => x.IsSelected);
            var finalDataGridCount = SessionDataGrid?.SelectedItems?.Count ?? 0;
            LogSessionMessage($"DELETE FINAL COUNTS: IsSelected={finalIsSelectedCount}, DataGrid={finalDataGridCount}, DeleteTargets={selectedItems.Count}");
            
            if (selectedItems.Count == 0)
            {
                LogSessionMessage("No captures selected for deletion");
                return;
            }
            
            // Confirmation dialog for multiple items
            if (selectedItems.Count > 1)
            {
                var result = MessageBox.Show(
                    $"Delete {selectedItems.Count} images? (Files will be moved to recycle folder)",
                    "Confirm Deletion",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                    
                if (result != MessageBoxResult.Yes)
                    return;
            }
            
            try
            {
                var imageIds = selectedItems.Where(x => !string.IsNullOrEmpty(x.ImageId))
                                          .Select(x => x.ImageId)
                                          .ToList();
                
                if (imageIds.Count > 0)
                {
                    // Step 1B: Log delete targets before repository call
                    var targetList = selectedItems.Take(10).Select(item => 
                        $"{item.PartNumber}.{item.Sequence:000}({item.ImageId?.Substring(0, Math.Min(8, item.ImageId?.Length ?? 0)) ?? "NO-ID"})"
                    ).ToList();
                    var moreText = selectedItems.Count > 10 ? $" +{selectedItems.Count - 10} more" : "";
                    LogSessionMessage($"DELETE TARGETS: {imageIds.Count} IDs, items=[{string.Join(", ", targetList)}{moreText}]");
                    
                    // Build undo action before soft delete
                    var undoAction = new UndoAction
                    {
                        ActionId = Guid.NewGuid().ToString(),
                        Timestamp = DateTime.UtcNow,
                        ActionType = UndoActionType.Delete,
                        Description = $"Deleted {selectedItems.Count} images",
                        RecycleFolderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Exports", ".recycle")
                    };
                    
                    // Capture original state for each image
                    foreach (var item in selectedItems.Where(x => !string.IsNullOrEmpty(x.ImageId)))
                    {
                        try
                        {
                            // Get full database record
                            var dbRecord = _repository.GetImageById(item.ImageId);
                            if (dbRecord != null)
                            {
                                var deletedImageInfo = new DeletedImageInfo
                                {
                                    OriginalFullPath = dbRecord.FullPath,
                                    OriginalThumbPath = dbRecord.ThumbPath,
                                    RecycledFullPath = Path.Combine(undoAction.RecycleFolderPath, dbRecord.ImageId, Path.GetFileName(dbRecord.FullPath)),
                                    RecycledThumbPath = string.IsNullOrEmpty(dbRecord.ThumbPath) ? null : Path.Combine(undoAction.RecycleFolderPath, dbRecord.ImageId, Path.GetFileName(dbRecord.ThumbPath)),
                                    OriginalDbRecord = dbRecord,
                                    OriginalIndex = _imageRecords.IndexOf(item)
                                };
                                
                                undoAction.DeletedImages.Add(deletedImageInfo);
                            }
                        }
                        catch (Exception ex)
                        {
                            LogSessionMessage($"Failed to capture undo info for {item.PartNumber}.{item.Sequence}: {ex.Message}");
                        }
                    }
                    
                    // Soft delete from database and move files to recycle
                    _repository.SoftDeleteCaptures(imageIds, LogSessionMessage);
                    
                    // Push undo action after successful delete
                    _undoStack.Push(undoAction);
                    
                    // Limit stack to 20 actions
                    while (_undoStack.Count > 20)
                    {
                        _undoStack.Pop();
                    }
                    
                    LogSessionMessage($"DELETE UNDO: Pushed action {undoAction.ActionId} with {undoAction.DeletedImages.Count} items to stack (stack size: {_undoStack.Count})");
                }
                
                // Remove from UI collection
                foreach (var item in selectedItems)
                {
                    _imageRecords.Remove(item);
                }
                
                // Select next item if available
                if (_imageRecords.Count > 0)
                {
                    var nextItem = _imageRecords.FirstOrDefault();
                    if (nextItem != null)
                    {
                        SyncSelection(nextItem);
                    }
                }
                
                LogSessionMessage($"Soft deleted {selectedItems.Count} captures successfully");
                StatusTextBlock.Text = $"Soft deleted {selectedItems.Count} captures";
            }
            catch (Exception ex)
            {
                LogSessionMessage($"Delete operation failed: {ex.Message}");
                MessageBox.Show($"Delete failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        /// <summary>
        /// Undo the last soft delete operation
        /// </summary>
        private void UndoLastDelete()
        {
            if (_undoStack.Count == 0)
            {
                LogSessionMessage("UNDO: No actions to undo");
                return;
            }
            
            if (_database == null || _repository == null)
            {
                LogSessionMessage("UNDO: Database not available");
                return;
            }
            
            try
            {
                var undoAction = _undoStack.Pop();
                var restoredCount = 0;
                
                foreach (var deletedImage in undoAction.DeletedImages)
                {
                    try
                    {
                        // Restore DB record
                        _repository.RestoreCapturedImage(deletedImage.OriginalDbRecord.ImageId);
                        
                        // Move files back from recycle to original paths
                        if (File.Exists(deletedImage.RecycledFullPath) && !string.IsNullOrEmpty(deletedImage.OriginalFullPath))
                        {
                            var originalDir = Path.GetDirectoryName(deletedImage.OriginalFullPath);
                            if (!Directory.Exists(originalDir))
                                Directory.CreateDirectory(originalDir);
                            File.Move(deletedImage.RecycledFullPath, deletedImage.OriginalFullPath);
                        }
                        
                        if (File.Exists(deletedImage.RecycledThumbPath) && !string.IsNullOrEmpty(deletedImage.OriginalThumbPath))
                        {
                            var originalThumbDir = Path.GetDirectoryName(deletedImage.OriginalThumbPath);
                            if (!Directory.Exists(originalThumbDir))
                                Directory.CreateDirectory(originalThumbDir);
                            File.Move(deletedImage.RecycledThumbPath, deletedImage.OriginalThumbPath);
                        }
                        
                        // Recreate view model and reinsert into UI collection
                        var restoredViewModel = ImageRecordViewModel.FromCapturedImage(deletedImage.OriginalDbRecord);
                        LoadThumbnailForViewModel(restoredViewModel);
                        
                        // Insert at original index if possible, otherwise at the beginning
                        var insertIndex = Math.Min(deletedImage.OriginalIndex, _imageRecords.Count);
                        _imageRecords.Insert(insertIndex, restoredViewModel);
                        
                        restoredCount++;
                    }
                    catch (Exception ex)
                    {
                        LogSessionMessage($"UNDO: Failed to restore {deletedImage.OriginalDbRecord?.PartNumber}.{deletedImage.OriginalDbRecord?.Sequence}: {ex.Message}");
                    }
                }
                
                if (restoredCount > 0)
                {
                    LogSessionMessage($"UNDO: Restored {restoredCount} images");
                    StatusTextBlock.Text = $"Restored {restoredCount} images";
                }
                else
                {
                    LogSessionMessage("UNDO: Failed to restore any images");
                }
            }
            catch (Exception ex)
            {
                LogSessionMessage($"UNDO: Failed - {ex.Message}");
            }
        }
        /// <summary>
        /// Check if the current focus is on a text input control
        /// </summary>
        private bool IsTextInputContext()
        {
            var focused = Keyboard.FocusedElement;
            return focused is TextBox || focused is ComboBox ||
                   (focused is Control control && control.IsTabStop &&
                    (control.GetType().Name.Contains("Text") || control.GetType().Name.Contains("Edit")));
        }

        /// <summary>
        /// Phase 3.9: Handle key events for deletion and undo
        /// </summary>
        /// <summary>
        /// Phase 3.9: Handle key events for deletion and undo
        /// </summary>
        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            // Handle Ctrl+Z for undo
            if (e.Key == Key.Z && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                if (!IsTextInputContext())
                {
                    LogSessionMessage("KEY: Ctrl+Z pressed → calling UndoLastDelete");
                    UndoLastDelete();
                    e.Handled = true;
                    return;
                }
                else
                {
                    var focusedElementType = Keyboard.FocusedElement?.GetType()?.Name ?? "UNKNOWN";
                    LogSessionMessage($"KEY BLOCKED: text input focus ({focusedElementType}) - Ctrl+Z ignored");
                }
            }
            
            // Handle Delete and Backspace for deletion
            if (e.Key == Key.Delete || e.Key == Key.Back)
            {
                // Block delete while typing in text inputs
                if (IsTextInputContext())
                {
                    var focusedElementType = Keyboard.FocusedElement?.GetType()?.Name ?? "UNKNOWN";
                    LogSessionMessage($"KEY BLOCKED: text input focus ({focusedElementType}) - Delete ignored");
                    return;
                }
                
                // Check if there are selected items in either thumbnails or DataGrid
                var hasSelectedThumbnails = _imageRecords.Any(x => x.IsSelected);
                var hasSelectedDataGridItems = SessionDataGrid.SelectedItems.Count > 0;
                
                if (hasSelectedThumbnails || hasSelectedDataGridItems)
                {
                    LogSessionMessage($"KEY: Delete pressed → calling DeleteSelectedCaptures (thumbnails={hasSelectedThumbnails}, datagrid={hasSelectedDataGridItems})");
                    DeleteSelectedCaptures();
                    e.Handled = true;
                    return;
                }
            }
            
            base.OnPreviewKeyDown(e);
        }
        
        /// <summary>
        /// Helper to check if element is descendant of parent
        /// </summary>
        private bool IsDescendantOf(DependencyObject child, DependencyObject parent)
        {
            if (child == null || parent == null) return false;
            
            var current = child;
            while (current != null)
            {
                if (current == parent) return true;
                current = VisualTreeHelper.GetParent(current);
            }
            return false;
        }
        
        #endregion
    }
}
