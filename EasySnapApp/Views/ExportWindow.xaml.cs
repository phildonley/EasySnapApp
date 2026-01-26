using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using EasySnapApp.Models;
using EasySnapApp.Data; // Use CaptureRepository
using EasySnapApp.Services;
using Microsoft.Win32;
using System.ComponentModel;
using System.Globalization;

namespace EasySnapApp.Views
{
    /// <summary>
    /// Wrapper class to make CapturedImage compatible with UI bindings
    /// </summary>
    public class ExportImageWrapper : INotifyPropertyChanged
    {
        private readonly CapturedImage _capturedImage;
        private bool _isSelected;

        public ExportImageWrapper(CapturedImage capturedImage)
        {
            _capturedImage = capturedImage;
        }

        // Original CapturedImage properties
        public string ImageId => _capturedImage.ImageId;
        public string PartNumber => _capturedImage.PartNumber;
        public int Sequence => _capturedImage.Sequence;
        public string FullPath => _capturedImage.FullPath;
        public string ThumbPath => _capturedImage.ThumbPath;
        public DateTime CaptureTimeUtc => _capturedImage.CaptureTimeUtc;
        public long FileSizeBytes => _capturedImage.FileSizeBytes;
        public double? WeightGrams => _capturedImage.WeightGrams;
        public double? DimX => _capturedImage.DimX;
        public double? DimY => _capturedImage.DimY;
        public double? DimZ => _capturedImage.DimZ;

        // UI-friendly properties
        public string FileName => Path.GetFileName(_capturedImage.FullPath);
        public string FileSizeMB => $"{_capturedImage.FileSizeBytes / (1024.0 * 1024.0):F1} MB";
        public string DisplayName => $"{_capturedImage.PartNumber}.{_capturedImage.Sequence:000}";
        public string DimensionsDisplay => 
            _capturedImage.DimX.HasValue || _capturedImage.DimY.HasValue || _capturedImage.DimZ.HasValue
                ? $"{_capturedImage.DimX?.ToString("F2") ?? "?"}×{_capturedImage.DimY?.ToString("F2") ?? "?"}×{_capturedImage.DimZ?.ToString("F2") ?? "?"}"
                : "N/A";
        
        public double? Weight => _capturedImage.WeightGrams.HasValue 
            ? _capturedImage.WeightGrams.Value * 0.00220462 // Convert grams to pounds
            : null;

        // Selection property for UI
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
                }
            }
        }

        // Access to original object for export operations
        public CapturedImage CapturedImage => _capturedImage;

        public event PropertyChangedEventHandler PropertyChanged;
    }

    public partial class ExportWindow : Window
    {
        private readonly CaptureRepository _repository;
        private readonly ExportService _exportService;
        private ObservableCollection<ExportImageWrapper> _images;
        private List<CapturedImage> _allImages;
        private CancellationTokenSource _exportCancellation;
        private readonly string _databasePath;

        /// <summary>
        /// Constructor that accepts the repository instance to use the same database as MainWindow
        /// </summary>
        /// <param name="repository">CaptureRepository instance from MainWindow</param>
        public ExportWindow(CaptureRepository repository = null)
        {
            InitializeComponent();
            
            // Use the passed repository or create a new one
            _repository = repository;
            if (_repository == null)
            {
                var database = new EasySnapDb();
                _repository = new CaptureRepository(database);
                _databasePath = database.DatabasePath;
            }
            else
            {
                _databasePath = "MainWindow Repository";
            }
            
            _exportService = new ExportService();
            _images = new ObservableCollection<ExportImageWrapper>();
            dgImages.ItemsSource = _images;

            // Set up event handlers AFTER InitializeComponent to avoid null reference issues
            slQuality.ValueChanged += (s, e) => lblQualityValue.Text = $"{(int)slQuality.Value}%";
            
            // Hook up the SizeMode selection event handler AFTER controls are initialized
            cmbSizeMode.SelectionChanged += SizeMode_SelectionChanged;
            
            // Initialize the size mode panels visibility based on current selection
            UpdateSizeModePanels();
            
            // Load real data from database
            LoadDataAsync();
        }

        private void LoadDataAsync()
        {
            try
            {
                // Load all real images from database (no sample data)
                _allImages = _repository.GetAllImages();
                
                // Load part numbers from real data
                var partNumbers = _repository.GetDistinctPartNumbers();
                cmbPartNumbers.Items.Clear();
                cmbPartNumbers.Items.Add("All Parts");
                foreach (var partNumber in partNumbers)
                {
                    cmbPartNumbers.Items.Add(partNumber);
                }
                
                if (cmbPartNumbers.Items.Count > 0)
                    cmbPartNumbers.SelectedIndex = 0;
                
                RefreshImagesList();
                
                // Update status
                var totalImages = _allImages?.Count ?? 0;
                if (totalImages == 0)
                {
                    MessageBox.Show("No images found in database.\n\nCapture some images first, then use Export.", 
                        "No Data", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading data from database: {ex.Message}\n\nDatabase path: {_databasePath}", 
                    "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RefreshImagesList()
        {
            _images.Clear();
            
            if (_allImages == null)
                return;
            
            var selectedPartNumber = cmbPartNumbers.SelectedItem?.ToString();
            var imagesToShow = string.IsNullOrEmpty(selectedPartNumber) || selectedPartNumber == "All Parts"
                ? _allImages
                : _allImages.Where(i => i.PartNumber == selectedPartNumber).ToList();

            // Sort by part number, then by sequence for stable order
            foreach (var image in imagesToShow.OrderBy(i => i.PartNumber).ThenBy(i => i.Sequence))
            {
                _images.Add(new ExportImageWrapper(image));
            }

            UpdateStatusLabels();
        }

        private void UpdateStatusLabels()
        {
            lblImageCount.Text = $"{_images.Count} images loaded";
            var selectedCount = _images.Count(i => i.IsSelected);
            lblSelectedCount.Text = $"{selectedCount} selected";
            
            // Enable buttons based on selection and output folder
            var hasOutput = !string.IsNullOrWhiteSpace(txtOutputFolder.Text);
            btnExport.IsEnabled = selectedCount > 0 && hasOutput;
            btnExportDims.IsEnabled = _images.Count > 0 && hasOutput; // Export dims works with all or selected
        }

        /// <summary>
        /// Update the visibility of size mode panels with null guards to prevent initialization crashes
        /// </summary>
        private void UpdateSizeModePanels()
        {
            // Null guards - controls might not be initialized during construction
            if (cmbSizeMode?.SelectedItem is ComboBoxItem item && pnlLongEdge != null && pnlFitInside != null)
            {
                var mode = item.Tag?.ToString();
                pnlLongEdge.Visibility = mode == "LongEdge" ? Visibility.Visible : Visibility.Collapsed;
                pnlFitInside.Visibility = mode == "FitInside" ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        #region Event Handlers

        private void PartNumbers_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            RefreshImagesList();
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var image in _images)
            {
                image.IsSelected = true;
            }
            dgImages.Items.Refresh();
            UpdateStatusLabels();
        }

        private void SelectNone_Click(object sender, RoutedEventArgs e)
        {
            foreach (var image in _images)
            {
                image.IsSelected = false;
            }
            dgImages.Items.Refresh();
            UpdateStatusLabels();
        }

        private void DataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (dgImages.SelectedItem is ExportImageWrapper selectedWrapper)
            {
                LoadImagePreview(selectedWrapper.CapturedImage);
            }
            UpdateStatusLabels();
        }

        private void BrowseOutputFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new System.Windows.Forms.FolderBrowserDialog()
            {
                Description = "Select output folder for exported images",
                ShowNewFolderButton = true
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                txtOutputFolder.Text = dialog.SelectedPath;
                UpdateStatusLabels();
            }
        }

        private void SizeMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Update panels with null safety
            UpdateSizeModePanels();
        }

        private async void Export_Click(object sender, RoutedEventArgs e)
        {
            var selectedWrappers = _images.Where(i => i.IsSelected).ToList();
            if (!selectedWrappers.Any())
            {
                MessageBox.Show("Please select at least one image to export.", "No Selection", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Convert wrappers to ImageRecord for export service
            var selectedImages = selectedWrappers.Select(w => ConvertToImageRecord(w.CapturedImage)).ToList();

            if (string.IsNullOrWhiteSpace(txtOutputFolder.Text))
            {
                MessageBox.Show("Please select an output folder.", "No Output Folder", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var options = CreateExportOptions();
                
                // Show progress panel
                pnlProgress.Visibility = Visibility.Visible;
                btnExport.IsEnabled = false;
                btnExportDims.IsEnabled = false;
                btnCancel.Content = "Cancel Export";
                
                _exportCancellation = new CancellationTokenSource();
                
                var progress = new Progress<ExportProgressEventArgs>(OnExportProgress);
                
                var success = await _exportService.ExportAsync(selectedImages, options, progress, _exportCancellation.Token);
                
                if (success)
                {
                    MessageBox.Show($"Export completed successfully!\n\nImages exported to: {options.OutputFolder}", 
                        "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show("Export completed with some errors. Check progress messages for details.", 
                        "Export Complete", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (OperationCanceledException)
            {
                MessageBox.Show("Export was cancelled.", "Export Cancelled", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Export failed: {ex.Message}", "Export Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                // Hide progress and reset UI
                pnlProgress.Visibility = Visibility.Collapsed;
                btnExport.IsEnabled = true;
                btnExportDims.IsEnabled = true;
                btnCancel.Content = "Cancel";
                _exportCancellation?.Dispose();
                _exportCancellation = null;
            }
        }

        private void ExportDims_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtOutputFolder.Text))
            {
                MessageBox.Show("Please select an output folder.", "No Output Folder", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                // Get all available images for selection
                var allImages = _allImages ?? new List<CapturedImage>();
                if (!allImages.Any())
                {
                    MessageBox.Show("No images available to export.", "No Data", 
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Show selection dialog
                var selectionWindow = new DimsExportSelectionWindow(allImages)
                {
                    Owner = this
                };
                
                if (selectionWindow.ShowDialog() != true)
                    return; // User cancelled
                
                var imagesToExport = selectionWindow.SelectedImages;
                if (!imagesToExport.Any())
                    return;

                // Load DIMS export settings
                var dimsSettings = DimsExportSettings.Load();
                
                // Create CSV filename using DIMS settings format: {SITE_ID}_{yyyyMMdd}_{HHmmss}.csv
                var now = DateTime.Now;
                var timestamp = now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
                var csvFileName = $"{dimsSettings.SiteId}_{timestamp}.csv";
                var csvPath = Path.Combine(txtOutputFolder.Text, csvFileName);

                // Export dimensions CSV with DIMS settings
                ExportDimensionsCsvWithSettings(imagesToExport, csvPath, dimsSettings);

                var partCount = imagesToExport.GroupBy(i => i.PartNumber).Count();
                MessageBox.Show($"DIMS export completed successfully!\n\nFile: {csvFileName}\nLocation: {txtOutputFolder.Text}\n\nExported: {imagesToExport.Count} images from {partCount} parts", 
                    "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"DIMS export failed: {ex.Message}", "Export Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            if (_exportCancellation != null)
            {
                _exportCancellation.Cancel();
            }
            else
            {
                DialogResult = false;
            }
        }

        #endregion

        #region Helper Methods

        private ExportOptions CreateExportOptions()
        {
            var options = new ExportOptions
            {
                OutputFolder = txtOutputFolder.Text,
                JpegQuality = (int)slQuality.Value,
                CreateZip = chkCreateZip.IsChecked == true,
                IncludeManifest = chkCreateManifest.IsChecked == true
            };

            if (cmbSizeMode.SelectedItem is ComboBoxItem item)
            {
                switch (item.Tag?.ToString())
                {
                    case "Original":
                        options.SizeMode = ExportSizeMode.Original;
                        break;
                    case "LongEdge":
                        options.SizeMode = ExportSizeMode.LongEdge;
                        if (int.TryParse(txtLongEdge.Text, out int longEdge))
                            options.LongEdgePixels = longEdge;
                        break;
                    case "FitInside":
                        options.SizeMode = ExportSizeMode.FitInside;
                        if (int.TryParse(txtFitWidth.Text, out int fitWidth))
                            options.FitWidth = fitWidth;
                        if (int.TryParse(txtFitHeight.Text, out int fitHeight))
                            options.FitHeight = fitHeight;
                        break;
                }
            }

            return options;
        }

        private void OnExportProgress(ExportProgressEventArgs args)
        {
            Dispatcher.Invoke(() =>
            {
                lblProgressMessage.Text = args.Message;
                pbProgress.Value = args.PercentComplete;
                
                if (args.HasError)
                {
                    lblProgressMessage.Foreground = System.Windows.Media.Brushes.Red;
                }
                else if (args.IsCompleted)
                {
                    lblProgressMessage.Foreground = System.Windows.Media.Brushes.Green;
                }
                else
                {
                    lblProgressMessage.Foreground = System.Windows.Media.Brushes.Black;
                }
            });
        }

        /// <summary>
        /// Load image preview safely without locking files
        /// </summary>
        private void LoadImagePreview(CapturedImage image)
        {
            try
            {
                if (File.Exists(image.FullPath))
                {
                    // Use safe loading that doesn't lock the file
                    var bitmap = LoadImageSafely(image.FullPath);
                    imgPreview.Source = bitmap;
                    lblPreviewFileName.Text = $"{image.PartNumber}.{image.Sequence:000}";
                    
                    var fileSizeMB = $"{image.FileSizeBytes / (1024.0 * 1024.0):F1} MB";
                    var weight = image.WeightGrams.HasValue ? $"{image.WeightGrams.Value * 0.00220462:F2} lb" : "N/A";
                    var dimensions = (image.DimX.HasValue || image.DimY.HasValue || image.DimZ.HasValue)
                        ? $"{image.DimX?.ToString("F2") ?? "?"}×{image.DimY?.ToString("F2") ?? "?"}×{image.DimZ?.ToString("F2") ?? "?"}"
                        : "N/A";
                    
                    lblPreviewMetadata.Text = $"{Path.GetFileName(image.FullPath)}\n" +
                                            $"Captured: {image.CaptureTimeUtc:yyyy-MM-dd HH:mm:ss}\n" +
                                            $"Size: {fileSizeMB}\n" +
                                            $"Weight: {weight}\n" +
                                            $"Dimensions: {dimensions}";
                }
                else
                {
                    imgPreview.Source = null;
                    lblPreviewFileName.Text = "Missing file";
                    lblPreviewMetadata.Text = $"File not found: {image.FullPath}\n\n" +
                                            $"Part: {image.PartNumber}\n" +
                                            $"Sequence: {image.Sequence}\n" +
                                            $"Captured: {image.CaptureTimeUtc:yyyy-MM-dd HH:mm:ss}";
                }
            }
            catch (Exception ex)
            {
                imgPreview.Source = null;
                lblPreviewFileName.Text = "Error loading preview";
                lblPreviewMetadata.Text = $"Error: {ex.Message}\n\nFile: {image.FullPath}";
            }
        }

        /// <summary>
        /// File-safe image loading that doesn't lock files (same method from MainWindow)
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
                bitmap.DecodePixelWidth = 300; // Limit preview size for performance
                bitmap.EndInit();
                bitmap.Freeze(); // Make it cross-thread safe
                
                return bitmap;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load image safely: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Export DIMS CSV with configurable settings (25-column template format)
        /// </summary>
        private void ExportDimensionsCsvWithSettings(List<CapturedImage> images, string csvPath, DimsExportSettings settings)
        {
            var csv = new StringBuilder();
            
            // DIMS CSV Header (25 columns matching template)
            csv.AppendLine("SITE_ID,ITEM_ID,NET_LENGTH,NET_WIDTH,NET_HEIGHT,NET_WEIGHT,NET_VOLUME,NET_DIM_WGT," +
                          "GROSS_LENGTH,GROSS_WIDTH,GROSS_HEIGHT,GROSS_WEIGHT,GROSS_VOLUME,GROSS_DIM_WGT," +
                          "DIM_UNIT,WGT_UNIT,VOL_UNIT,FACTOR,OPT_INFO_1,OPT_INFO_2,OPT_INFO_3,OPT_INFO_4," +
                          "OPT_INFO_5,OPT_INFO_6,OPT_INFO_7,OPT_INFO_8,TIME_STAMP,IMAGE_FILE_NAME,UPDATED");
            
            foreach (var image in images.OrderBy(i => i.PartNumber).ThenBy(i => i.Sequence))
            {
                // Derived fields from capture data
                var itemId = image.PartNumber ?? "";
                var netLength = image.DimX?.ToString("F4", CultureInfo.InvariantCulture) ?? "";
                var netWidth = image.DimY?.ToString("F4", CultureInfo.InvariantCulture) ?? "";
                var netHeight = image.DimZ?.ToString("F4", CultureInfo.InvariantCulture) ?? "";
                var netWeight = image.WeightGrams.HasValue 
                    ? (image.WeightGrams.Value * 0.00220462).ToString("F4", CultureInfo.InvariantCulture) // Convert g to lb
                    : "";
                    
                // Calculate NET_VOLUME (L × W × H) if all dimensions present
                var netVolume = "";
                if (image.DimX.HasValue && image.DimY.HasValue && image.DimZ.HasValue)
                {
                    var volume = image.DimX.Value * image.DimY.Value * image.DimZ.Value;
                    netVolume = volume.ToString("F4", CultureInfo.InvariantCulture);
                }
                
                // Calculate NET_DIM_WGT (NET_VOLUME / FACTOR) if volume present
                var netDimWgt = "";
                if (!string.IsNullOrEmpty(netVolume) && double.TryParse(settings.Factor, out var factor) && factor > 0)
                {
                    var dimWgt = double.Parse(netVolume, CultureInfo.InvariantCulture) / factor;
                    netDimWgt = dimWgt.ToString("F4", CultureInfo.InvariantCulture);
                }
                
                var timeStamp = image.CaptureTimeUtc.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture);
                var imageFileName = Path.GetFileName(image.FullPath) ?? "";
                
                // Build CSV row with all 25 columns + 3 additional (28 total)
                var csvRow = string.Join(",", new string[]
                {
                    settings.SiteId ?? "733",                    // SITE_ID
                    $"\"{itemId}\"",                          // ITEM_ID
                    netLength,                                   // NET_LENGTH
                    netWidth,                                    // NET_WIDTH
                    netHeight,                                   // NET_HEIGHT
                    netWeight,                                   // NET_WEIGHT
                    netVolume,                                   // NET_VOLUME
                    netDimWgt,                                   // NET_DIM_WGT
                    "",                                          // GROSS_LENGTH (empty)
                    "",                                          // GROSS_WIDTH (empty)
                    "",                                          // GROSS_HEIGHT (empty)
                    "",                                          // GROSS_WEIGHT (empty)
                    "",                                          // GROSS_VOLUME (empty)
                    "",                                          // GROSS_DIM_WGT (empty)
                    settings.DimUnit ?? "in",                   // DIM_UNIT
                    settings.WgtUnit ?? "lb",                   // WGT_UNIT
                    settings.VolUnit ?? "in",                   // VOL_UNIT
                    settings.Factor ?? "166",                   // FACTOR
                    "",                                          // OPT_INFO_1 (empty)
                    settings.OptInfo2 ?? "Y",                   // OPT_INFO_2
                    settings.OptInfo3 ?? "Y",                   // OPT_INFO_3
                    "",                                          // OPT_INFO_4 (empty)
                    "",                                          // OPT_INFO_5 (empty)
                    "",                                          // OPT_INFO_6 (empty)
                    "",                                          // OPT_INFO_7 (empty)
                    settings.OptInfo8 ?? "0",                   // OPT_INFO_8
                    timeStamp,                                   // TIME_STAMP
                    $"\"{imageFileName}\"",                   // IMAGE_FILE_NAME
                    settings.Updated ?? "N"                     // UPDATED
                });
                
                csv.AppendLine(csvRow);
            }

            File.WriteAllText(csvPath, csv.ToString(), Encoding.UTF8);
        }

        /// <summary>
        /// Export simple dimensions CSV (legacy method - kept for compatibility)
        /// </summary>
        private void ExportDimensionsCsv(List<CapturedImage> images, string csvPath)
        {
            var csv = new StringBuilder();
            
            // Simple CSV Header
            csv.AppendLine("PartNumber,Sequence,CaptureTimeUtc,Weight,Length,Width,Height,ImageFileName");
            
            foreach (var image in images.OrderBy(i => i.PartNumber).ThenBy(i => i.Sequence))
            {
                // Map DimX/Y/Z to Length/Width/Height
                var length = image.DimX?.ToString("F2") ?? "";
                var width = image.DimY?.ToString("F2") ?? "";  
                var height = image.DimZ?.ToString("F2") ?? "";
                var weight = image.WeightGrams.HasValue ? (image.WeightGrams.Value * 0.00220462).ToString("F2") : "";
                
                csv.AppendLine($"\"{image.PartNumber}\",{image.Sequence},{image.CaptureTimeUtc:yyyy-MM-dd HH:mm:ss}," +
                              $"{weight},{length},{width},{height},\"{Path.GetFileName(image.FullPath)}\"");
            }

            File.WriteAllText(csvPath, csv.ToString());
        }

        /// <summary>
        /// Convert CapturedImage to ImageRecord for export service compatibility
        /// </summary>
        private ImageRecord ConvertToImageRecord(CapturedImage capturedImage)
        {
            return new ImageRecord
            {
                Id = capturedImage.ImageId.GetHashCode(), // Simple conversion for ID
                PartNumber = capturedImage.PartNumber,
                Sequence = capturedImage.Sequence,
                FullPath = capturedImage.FullPath,
                FileName = Path.GetFileName(capturedImage.FullPath),
                CaptureTimeUtc = capturedImage.CaptureTimeUtc,
                FileSizeBytes = capturedImage.FileSizeBytes,
                Weight = capturedImage.WeightGrams.HasValue ? capturedImage.WeightGrams.Value * 0.00220462 : null, // Convert g to lb
                DimX = capturedImage.DimX,
                DimY = capturedImage.DimY,
                DimZ = capturedImage.DimZ,
                Metadata = $"Session: {capturedImage.SessionId}"
            };
        }

        #endregion
    }
}
