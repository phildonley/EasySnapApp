using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Win32;
using EasySnapApp.Data;

namespace EasySnapApp.Views
{
    /// <summary>
    /// PHASE 1: Data Import Window for CSV/Excel parts data
    /// Supports large datasets (170k+ records) with dynamic column selection
    /// </summary>
    public partial class DataImportWindow : Window, INotifyPropertyChanged
    {
        #region Fields and Properties

        private readonly PartsDataRepository _partsRepository;
        private ImportPreviewData _currentPreviewData;
        private ImportMapping _currentMapping;
        private bool _isImporting = false;
        private readonly List<string> _importLogEntries = new List<string>();

        // Column selection view model
        public class ColumnSelectionItem : INotifyPropertyChanged
        {
            public string DisplayName { get; set; }
            public int Index { get; set; }
            public bool IsSelected { get; set; }

            public event PropertyChangedEventHandler PropertyChanged;
            protected void OnPropertyChanged(string name)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            }
        }

        private ObservableCollection<ColumnSelectionItem> _availableColumns = new ObservableCollection<ColumnSelectionItem>();
        private ObservableCollection<ColumnSelectionItem> _filteredColumns = new ObservableCollection<ColumnSelectionItem>();

        public event PropertyChangedEventHandler PropertyChanged;

        #endregion

        #region Constructor and Initialization

        public DataImportWindow(PartsDataRepository partsRepository)
        {
            _partsRepository = partsRepository ?? throw new ArgumentNullException(nameof(partsRepository));

            InitializeComponent();

            _currentMapping = new ImportMapping();

            // Initialize UI
            InitializeUI();

            LogMessage("Data Import Window initialized. Ready to import parts data.");
        }

        private void InitializeUI()
        {
            AdditionalColumnsPanel.ItemsSource = _filteredColumns;

            // Set default header row selection (row 2, which is index 1)
            HeaderRowComboBox.SelectedIndex = 1;

            UpdateStatus("Select a CSV or Excel file to begin import");
        }

        #endregion

        #region File Selection and Analysis

        private void BrowseButton_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Title = "Select Parts Data File",
                Filter = "CSV files (*.csv)|*.csv|Excel files (*.xlsx)|*.xlsx|All files (*.*)|*.*",
                FilterIndex = 1
            };

            if (openFileDialog.ShowDialog() == true)
            {
                FilePathTextBox.Text = openFileDialog.FileName;
                AnalyzeButton.IsEnabled = true;

                LogMessage($"Selected file: {Path.GetFileName(openFileDialog.FileName)}");
                UpdateStatus("File selected. Click 'Analyze' to examine the data structure.");

                // Reset UI state
                ResetImportState();
            }
        }

        private void AnalyzeButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(FilePathTextBox.Text) || !File.Exists(FilePathTextBox.Text))
            {
                MessageBox.Show("Please select a valid file first.", "File Not Found",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                UpdateStatus("Analyzing file structure...");
                AnalyzeButton.IsEnabled = false;

                var filePath = FilePathTextBox.Text;
                var headerRowIndex = HeaderRowComboBox.SelectedIndex;

                // Parse file synchronously on UI thread (simpler, no threading issues)
                _currentPreviewData = _partsRepository.ParseCsvWithHeaderRow(filePath, headerRowIndex, 10);

                // Update UI with file info
                var fileInfo = new FileInfo(filePath);
                var fileSize = fileInfo.Length < 1024 * 1024
                    ? $"{fileInfo.Length / 1024:N0} KB"
                    : $"{fileInfo.Length / (1024 * 1024):N1} MB";

                FileInfoTextBlock.Text = $"Size: {fileSize}, Modified: {fileInfo.LastWriteTime:yyyy-MM-dd HH:mm}";
                TotalRowsTextBlock.Text = $"{_currentPreviewData.TotalRowCount:N0}";

                // Populate column selection UI
                PopulateColumnSelection();

                // Update preview
                RefreshDataPreview();

                // Enable next step
                RefreshPreviewButton.IsEnabled = true;

                LogMessage($"Analysis complete: {_currentPreviewData.Headers.Count} columns, {_currentPreviewData.TotalRowCount:N0} rows");
                UpdateStatus($"Analysis complete. Found {_currentPreviewData?.TotalRowCount ?? 0:N0} total rows.");
            }
            catch (Exception ex)
            {
                LogMessage($"Analysis failed: {ex.Message}");
                UpdateStatus($"Analysis failed: {ex.Message}");
                MessageBox.Show($"Failed to analyze file:\n\n{ex.Message}", "Analysis Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                AnalyzeButton.IsEnabled = true;
            }
        }

        private void AnalyzeFileStructure()
        {
            var filePath = FilePathTextBox.Text;
            var headerRowIndex = GetSelectedHeaderRowIndex();

            try
            {
                // Parse file for preview
                _currentPreviewData = _partsRepository.ParseCsvWithHeaderRow(filePath, headerRowIndex, 10);

                Dispatcher.Invoke(() =>
                {
                    // Update UI with file info
                    var fileInfo = new FileInfo(filePath);
                    var fileSize = fileInfo.Length < 1024 * 1024
                        ? $"{fileInfo.Length / 1024:N0} KB"
                        : $"{fileInfo.Length / (1024 * 1024):N1} MB";

                    FileInfoTextBlock.Text = $"Size: {fileSize}, Modified: {fileInfo.LastWriteTime:yyyy-MM-dd HH:mm}";
                    TotalRowsTextBlock.Text = $"{_currentPreviewData.TotalRowCount:N0}";

                    // Populate column selection UI
                    PopulateColumnSelection();

                    // Update preview
                    RefreshDataPreview();

                    // Enable next step
                    RefreshPreviewButton.IsEnabled = true;

                    LogMessage($"Analysis complete: {_currentPreviewData.Headers.Count} columns, {_currentPreviewData.TotalRowCount:N0} rows");
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() =>
                {
                    LogMessage($"File analysis error: {ex.Message}");
                });
                throw; // Move throw outside Dispatcher.Invoke
            }
        }

        #endregion

        #region Header Row Selection and Preview

        private void HeaderRowComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!string.IsNullOrEmpty(FilePathTextBox.Text) && File.Exists(FilePathTextBox.Text))
            {
                RefreshPreviewButton.IsEnabled = true;
            }
        }

        private async void RefreshPreviewButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(FilePathTextBox.Text) || !File.Exists(FilePathTextBox.Text))
                return;

            try
            {
                UpdateStatus("Refreshing preview with selected header row...");
                RefreshPreviewButton.IsEnabled = false;
                ShowPreviewStatus("Analyzing file structure...", true);

                // Re-analyze with new header row selection
                await Task.Run(() => AnalyzeFileStructure());

                HidePreviewStatus();
                UpdateStatus("Preview refreshed successfully.");
            }
            catch (Exception ex)
            {
                LogMessage($"Preview refresh failed: {ex.Message}");
                ShowPreviewStatus($"Error: {ex.Message}", true);
                UpdateStatus($"Preview refresh failed: {ex.Message}");
            }
            finally
            {
                RefreshPreviewButton.IsEnabled = true;
            }
        }

        #endregion

        #region Column Selection and Mapping

        private void PopulateColumnSelection()
        {
            if (_currentPreviewData?.Headers == null)
                return;

            // Clear existing selections
            _availableColumns.Clear();
            _filteredColumns.Clear();

            // Clear combo boxes
            ConnectionKeyColumnComboBox.Items.Clear();
            TmsIdColumnComboBox.Items.Clear();
            DisplayNameColumnComboBox.Items.Clear();

            // Add "Not Selected" option for optional columns
            ConnectionKeyColumnComboBox.Items.Add("(Select Column)");
            TmsIdColumnComboBox.Items.Add("(Not Selected)");
            DisplayNameColumnComboBox.Items.Add("(Not Selected)");

            // Populate all columns
            for (int i = 0; i < _currentPreviewData.Headers.Count; i++)
            {
                var header = _currentPreviewData.Headers[i];
                var displayName = string.IsNullOrEmpty(header) ? $"Column {i + 1}" : header;

                // Add to combo boxes
                ConnectionKeyColumnComboBox.Items.Add($"{i}: {displayName}");
                TmsIdColumnComboBox.Items.Add($"{i}: {displayName}");
                DisplayNameColumnComboBox.Items.Add($"{i}: {displayName}");

                // Add to additional columns list
                _availableColumns.Add(new ColumnSelectionItem
                {
                    DisplayName = displayName,
                    Index = i,
                    IsSelected = false
                });
            }

            // Apply filter (initially show all)
            ApplyColumnFilter("");

            // Auto-detect common columns
            AutoDetectColumns();

            LogMessage($"Populated {_currentPreviewData.Headers.Count} columns for selection");
        }

        private void AutoDetectColumns()
        {
            if (_currentPreviewData?.Headers == null)
                return;

            // Auto-detect Part Number column (look for "part", "partnumber", etc.)
            for (int i = 0; i < _currentPreviewData.Headers.Count; i++)
            {
                var header = _currentPreviewData.Headers[i]?.ToLower() ?? "";

                if (header.Contains("partnumber") || header.Contains("part_number") ||
                    (header.Contains("part") && header.Contains("number")))
                {
                    ConnectionKeyColumnComboBox.SelectedIndex = i + 1; // +1 for "(Select Column)" option
                    LogMessage($"Auto-detected Part Number column: {_currentPreviewData.Headers[i]}");
                    break;
                }
            }

            // Auto-detect TMS ID column (look for "id", "tms", etc.)
            for (int i = 0; i < _currentPreviewData.Headers.Count; i++)
            {
                var header = _currentPreviewData.Headers[i]?.ToLower() ?? "";

                if ((header == "id" || header == "tmsid" || header == "tms_id") &&
                    !header.Contains("part"))
                {
                    TmsIdColumnComboBox.SelectedIndex = i + 1; // +1 for "(Not Selected)" option
                    LogMessage($"Auto-detected TMS ID column: {_currentPreviewData.Headers[i]}");
                    break;
                }
            }

            // Auto-detect Display Name column (look for "display", "name", "description")
            for (int i = 0; i < _currentPreviewData.Headers.Count; i++)
            {
                var header = _currentPreviewData.Headers[i]?.ToLower() ?? "";

                if (header.Contains("display") || header == "name" ||
                    (header.Contains("display") && header.Contains("name")))
                {
                    DisplayNameColumnComboBox.SelectedIndex = i + 1; // +1 for "(Not Selected)" option
                    LogMessage($"Auto-detected Display Name column: {_currentPreviewData.Headers[i]}");
                    break;
                }
            }
        }

        private void ColumnMapping_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateMappingFromUI();
            ValidateMapping();
            UpdateSelectionSummary();
        }

        private void AdditionalColumn_CheckChanged(object sender, RoutedEventArgs e)
        {
            UpdateMappingFromUI();
            ValidateMapping();
            UpdateSelectionSummary();
        }

        private void ColumnSearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var searchTerm = ColumnSearchTextBox.Text?.Trim() ?? "";
            ApplyColumnFilter(searchTerm);
        }

        private void ApplyColumnFilter(string searchTerm)
        {
            _filteredColumns.Clear();

            var filteredItems = string.IsNullOrEmpty(searchTerm)
                ? _availableColumns
                : _availableColumns.Where(c => c.DisplayName.IndexOf(searchTerm, StringComparison.OrdinalIgnoreCase) >= 0);

            foreach (var item in filteredItems)
            {
                _filteredColumns.Add(item);
            }
        }

        private void UpdateMappingFromUI()
        {
            _currentMapping = new ImportMapping();

            // Part Number (required)
            if (ConnectionKeyColumnComboBox.SelectedIndex > 0)
            {
                _currentMapping.PartNumberColumnIndex = ConnectionKeyColumnComboBox.SelectedIndex - 1;
            }

            // TMS ID (optional)
            if (TmsIdColumnComboBox.SelectedIndex > 0)
            {
                _currentMapping.TmsIdColumnIndex = TmsIdColumnComboBox.SelectedIndex - 1;
            }

            // Display Name (optional)
            if (DisplayNameColumnComboBox.SelectedIndex > 0)
            {
                _currentMapping.DisplayNameColumnIndex = DisplayNameColumnComboBox.SelectedIndex - 1;
            }

            // Additional columns
            _currentMapping.AdditionalColumnIndexes = _availableColumns
                .Where(c => c.IsSelected)
                .Select(c => c.Index)
                .ToList();
        }

        private void ValidateMapping()
        {
            var isValid = _currentMapping.PartNumberColumnIndex >= 0;

            ValidateButton.IsEnabled = isValid && _currentPreviewData != null;
            ImportButton.IsEnabled = false; // Only enabled after validation

            if (!isValid)
            {
                UpdateStatus("Please select at least the Part Number column to continue.");
            }
        }

        private void UpdateSelectionSummary()
        {
            if (_currentMapping == null)
            {
                SelectionSummaryTextBlock.Text = "Select required columns to continue";
                return;
            }

            var selectedCount = 0;
            var summaryParts = new List<string>();

            if (_currentMapping.PartNumberColumnIndex >= 0)
            {
                summaryParts.Add("Part Number");
                selectedCount++;
            }

            if (_currentMapping.TmsIdColumnIndex >= 0)
            {
                summaryParts.Add("TMS ID");
                selectedCount++;
            }

            if (_currentMapping.DisplayNameColumnIndex >= 0)
            {
                summaryParts.Add("Display Name");
                selectedCount++;
            }

            selectedCount += _currentMapping.AdditionalColumnIndexes.Count;
            summaryParts.AddRange(_currentMapping.AdditionalColumnIndexes.Take(3)
                .Select(idx => _currentPreviewData.Headers[idx]));

            if (_currentMapping.AdditionalColumnIndexes.Count > 3)
            {
                summaryParts.Add($"...and {_currentMapping.AdditionalColumnIndexes.Count - 3} more");
            }

            SelectionSummaryTextBlock.Text = selectedCount > 0
                ? $"Selected {selectedCount} columns: {string.Join(", ", summaryParts)}"
                : "Select required columns to continue";
        }

        #endregion

        #region Data Preview

        private void RefreshDataPreview()
        {
            if (_currentPreviewData?.Headers == null || _currentPreviewData.PreviewRows == null)
            {
                PreviewDataGrid.ItemsSource = null;
                return;
            }

            try
            {
                // Create DataTable for preview
                var previewTable = new DataTable();

                // Add columns with proper headers
                for (int i = 0; i < _currentPreviewData.Headers.Count; i++)
                {
                    var header = _currentPreviewData.Headers[i];
                    var columnName = string.IsNullOrEmpty(header) ? $"Column{i + 1}" : header;
                    previewTable.Columns.Add(columnName, typeof(string));
                }

                // Add preview rows
                foreach (var row in _currentPreviewData.PreviewRows)
                {
                    var dataRow = previewTable.NewRow();
                    for (int i = 0; i < Math.Min(row.Count, previewTable.Columns.Count); i++)
                    {
                        dataRow[i] = row[i] ?? "";
                    }
                    previewTable.Rows.Add(dataRow);
                }

                PreviewDataGrid.ItemsSource = previewTable.DefaultView;
                LogMessage($"Preview updated: showing {previewTable.Rows.Count} rows of {_currentPreviewData.TotalRowCount:N0} total");
            }
            catch (Exception ex)
            {
                LogMessage($"Preview update failed: {ex.Message}");
                PreviewDataGrid.ItemsSource = null;
            }
        }

        #endregion

        #region Validation and Import

        private void ValidateButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                UpdateMappingFromUI();

                if (_currentMapping.PartNumberColumnIndex < 0)
                {
                    MessageBox.Show("Part Number column is required for import.", "Validation Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Additional validation
                var totalColumns = _currentMapping.AdditionalColumnIndexes.Count;
                if (_currentMapping.TmsIdColumnIndex >= 0) totalColumns++;
                if (_currentMapping.DisplayNameColumnIndex >= 0) totalColumns++;
                totalColumns++; // Part number

                var estimatedTime = EstimateImportTime(_currentPreviewData.TotalRowCount, totalColumns);
                var estimatedSize = EstimateDatabaseSize(_currentPreviewData.TotalRowCount, totalColumns);

                var message = $"Import Validation Summary:\n\n" +
                             $"• Total rows to import: {_currentPreviewData.TotalRowCount:N0}\n" +
                             $"• Selected columns: {totalColumns}\n" +
                             $"• Estimated time: {estimatedTime}\n" +
                             $"• Estimated database size: {estimatedSize}\n\n" +
                             $"This will replace any existing parts data. Continue?";

                var result = MessageBox.Show(message, "Confirm Import",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    ImportButton.IsEnabled = true;
                    ImportSummaryTextBlock.Text = $"Ready to import {_currentPreviewData.TotalRowCount:N0} rows with {totalColumns} columns";
                    UpdateStatus($"Validation successful. Ready to import {_currentPreviewData.TotalRowCount:N0} rows.");
                    LogMessage($"Import validation passed: {_currentPreviewData.TotalRowCount:N0} rows, {totalColumns} columns");
                }
            }
            catch (Exception ex)
            {
                LogMessage($"Validation failed: {ex.Message}");
                MessageBox.Show($"Validation failed:\n\n{ex.Message}", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void ImportButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isImporting)
            {
                MessageBox.Show("Import is already in progress.", "Import In Progress",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                _isImporting = true;
                ImportButton.Content = "Importing...";
                ImportButton.IsEnabled = false;

                // Show progress UI
                ImportProgressBar.Visibility = Visibility.Visible;
                ProgressTextBlock.Visibility = Visibility.Visible;
                ImportLogScrollViewer.Visibility = Visibility.Visible;

                LogMessage("Starting import process...");
                UpdateStatus("Importing parts data...");

                var filePath = FilePathTextBox.Text;
                var headerRowIndex = GetSelectedHeaderRowIndex();

                // Start import on background thread with progress reporting
                await Task.Run(() => ExecuteImport(filePath, headerRowIndex));

                // Import completed successfully
                ImportButton.Content = "Import Complete";
                ImportButton.Background = System.Windows.Media.Brushes.DarkGreen;
                ImportProgressBar.Value = 100;
                ProgressTextBlock.Text = "Import completed successfully!";

                UpdateStatus("Import completed successfully!");
                LogMessage("Import process completed successfully.");

                MessageBox.Show("Parts data import completed successfully!", "Import Complete",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                LogMessage($"Import failed: {ex.Message}");
                UpdateStatus($"Import failed: {ex.Message}");

                ImportButton.Content = "Import Failed";
                ImportButton.Background = System.Windows.Media.Brushes.DarkRed;
                ProgressTextBlock.Text = "Import failed!";

                MessageBox.Show($"Import failed:\n\n{ex.Message}", "Import Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _isImporting = false;
            }
        }

        private void ExecuteImport(string filePath, int headerRowIndex)
        {
            var startTime = DateTime.Now;

            _partsRepository.ImportCsvData(filePath, headerRowIndex, _currentMapping,
                progress =>
                {
                    Dispatcher.Invoke(() => UpdateImportProgress(progress));
                },
                batchSize: 2000); // Larger batch size for better performance

            var endTime = DateTime.Now;
            var duration = endTime - startTime;

            Dispatcher.Invoke(() =>
            {
                LogMessage($"Import completed in {duration.TotalSeconds:F1} seconds");
            });
        }

        private void UpdateImportProgress(ImportProgress progress)
        {
            if (progress.TotalRows > 0)
            {
                var percentage = (double)progress.ProcessedRows / progress.TotalRows * 100;
                ImportProgressBar.Value = percentage;

                var rate = progress.ElapsedTime.TotalSeconds > 0
                    ? progress.ProcessedRows / progress.ElapsedTime.TotalSeconds
                    : 0;

                var remainingRows = progress.TotalRows - progress.ProcessedRows;
                var estimatedRemainingSeconds = rate > 0 ? remainingRows / rate : 0;
                var eta = estimatedRemainingSeconds > 0 ? TimeSpan.FromSeconds(estimatedRemainingSeconds) : TimeSpan.Zero;

                ProgressTextBlock.Text = $"{progress.ProcessedRows:N0} / {progress.TotalRows:N0} rows " +
                                       $"({percentage:F1}%) - {rate:F0} rows/sec - ETA: {eta:mm\\:ss}";
            }

            // Log errors
            foreach (var error in progress.Errors.Skip(_importLogEntries.Count))
            {
                LogMessage($"ERROR: {error}");
            }

            if (progress.IsComplete)
            {
                LogMessage($"Import summary: {progress.SuccessfulImports:N0} imported, {progress.SkippedRows:N0} skipped, {progress.Errors.Count} errors");
            }
        }

        #endregion

        #region Helper Methods

        private int GetSelectedHeaderRowIndex()
        {
            var selectedItem = HeaderRowComboBox.SelectedItem as ComboBoxItem;
            var content = selectedItem?.Content?.ToString();

            switch (content)
            {
                case "None":
                    return -1; // No header row - generate column names
                case "Custom...":
                    return PromptForCustomHeaderRow();
                case "1": return 0;
                case "2": return 1;
                case "3": return 2;
                case "4": return 3;
                case "5": return 4;
                default: return HeaderRowComboBox.SelectedIndex; // Fallback to index
            }
        }

        private void ResetImportState()
        {
            _currentPreviewData = null;
            _currentMapping = new ImportMapping();

            // Reset UI
            TotalRowsTextBlock.Text = "0";
            FileInfoTextBlock.Text = "";
            PreviewDataGrid.ItemsSource = null;

            ConnectionKeyColumnComboBox.Items.Clear();
            TmsIdColumnComboBox.Items.Clear();
            DisplayNameColumnComboBox.Items.Clear();

            _availableColumns.Clear();
            _filteredColumns.Clear();

            RefreshPreviewButton.IsEnabled = false;
            ValidateButton.IsEnabled = false;
            ImportButton.IsEnabled = false;

            ImportProgressBar.Visibility = Visibility.Collapsed;
            ProgressTextBlock.Visibility = Visibility.Collapsed;
            ImportLogScrollViewer.Visibility = Visibility.Collapsed;

            HidePreviewStatus();
            SelectionSummaryTextBlock.Text = "Select required columns to continue";
            ImportSummaryTextBlock.Text = "";
        }

        private void ShowPreviewStatus(string message, bool isError = false)
        {
            PreviewStatusTextBlock.Text = message;
            PreviewStatusTextBlock.Foreground = isError
                ? System.Windows.Media.Brushes.Orange
                : System.Windows.Media.Brushes.LightBlue;
            PreviewStatusTextBlock.Visibility = Visibility.Visible;
        }

        private void HidePreviewStatus()
        {
            PreviewStatusTextBlock.Visibility = Visibility.Collapsed;
        }

        private void UpdateStatus(string message)
        {
            StatusTextBlock.Text = message;
        }

        private void LogMessage(string message)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            var logEntry = $"[{timestamp}] {message}";
            _importLogEntries.Add(logEntry);

            ImportLogTextBox.Text = string.Join("\n", _importLogEntries);
            ImportLogTextBox.ScrollToEnd();
        }

        private string EstimateImportTime(int rowCount, int columnCount)
        {
            // Rough estimate: 5000-10000 rows per second for typical CSV import
            var estimatedRate = Math.Max(5000, 10000 - (columnCount * 100)); // More columns = slower
            var estimatedSeconds = (double)rowCount / estimatedRate;

            if (estimatedSeconds < 60)
                return $"{estimatedSeconds:F0} seconds";
            else if (estimatedSeconds < 3600)
                return $"{estimatedSeconds / 60:F1} minutes";
            else
                return $"{estimatedSeconds / 3600:F1} hours";
        }

        private string EstimateDatabaseSize(int rowCount, int columnCount)
        {
            // Rough estimate: ~100 bytes per field average
            var estimatedBytes = (long)rowCount * columnCount * 100;

            if (estimatedBytes < 1024 * 1024)
                return $"{estimatedBytes / 1024:N0} KB";
            else if (estimatedBytes < 1024L * 1024 * 1024)
                return $"{estimatedBytes / (1024 * 1024):N0} MB";
            else
                return $"{estimatedBytes / (1024L * 1024 * 1024):N1} GB";
        }

        #endregion

        #region Window Events

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isImporting)
            {
                var result = MessageBox.Show("Import is in progress. Are you sure you want to cancel and close?",
                    "Import In Progress", MessageBoxButton.YesNo, MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes)
                    return;
            }

            Close();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (_isImporting)
            {
                var result = MessageBox.Show("Import is in progress. Are you sure you want to cancel and close?",
                    "Import In Progress", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result != MessageBoxResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }
            }
            base.OnClosing(e);
        }

        #region Search Box Event Handlers

        private void ColumnSearchTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (ColumnSearchTextBox.Text == "Search columns...")
            {
                ColumnSearchTextBox.Text = "";
                ColumnSearchTextBox.Foreground = System.Windows.Media.Brushes.White;
            }
        }

        private void ColumnSearchTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(ColumnSearchTextBox.Text))
            {
                ColumnSearchTextBox.Text = "Search columns...";
                ColumnSearchTextBox.Foreground = System.Windows.Media.Brushes.Gray;
            }
        }

        #endregion

        #region Enhanced Header Row Selection

        private int PromptForCustomHeaderRow()
        {
            string input = "";
            bool validInput = false;

            while (!validInput)
            {
                var dialog = new System.Windows.Forms.Form()
                {
                    Width = 300,
                    Height = 150,
                    FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedDialog,
                    Text = "Custom Header Row",
                    StartPosition = System.Windows.Forms.FormStartPosition.CenterParent,
                    MaximizeBox = false,
                    MinimizeBox = false
                };

                var textLabel = new System.Windows.Forms.Label() { Left = 20, Top = 20, Text = "Enter header row number:" };
                var textBox = new System.Windows.Forms.TextBox() { Left = 20, Top = 50, Width = 200, Text = "1" };
                var confirmButton = new System.Windows.Forms.Button() { Text = "OK", Left = 120, Width = 100, Top = 80, DialogResult = System.Windows.Forms.DialogResult.OK };

                dialog.Controls.Add(textLabel);
                dialog.Controls.Add(textBox);
                dialog.Controls.Add(confirmButton);
                dialog.AcceptButton = confirmButton;

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    input = textBox.Text;
                    if (int.TryParse(input, out int rowNum) && rowNum >= 1)
                    {
                        return rowNum - 1; // Convert to 0-based index
                    }
                    else
                    {
                        // Use WPF MessageBox (System.Windows.MessageBox)
                        System.Windows.MessageBox.Show("Please enter a valid row number (1 or greater).", "Invalid Input",
                                      MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                }
                else
                {
                    return 1; // Default to row 2 if cancelled
                }
            }

            return 1;
        }

        #endregion

        #endregion  
    }
}