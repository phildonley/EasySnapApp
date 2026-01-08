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
        // Kinect is intentionally not used in the main workflow anymore.
        private readonly KinectService _kinectSvc;
        private readonly CanonCameraService _cameraSvc;
        private readonly ThermalScannerService _thermalSvc;
        private readonly IntelIntellisenseService _intelSvc;
        private readonly LaserArrayService _laserSvc;

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

        public MainWindow()
        {
            InitializeComponent();

            // Prepare exports folder
            var exports = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Exports");
            Directory.CreateDirectory(exports);

            // Instantiate services
            var barcode = new BarcodeScannerService();
            var scale = new ScaleService(testMode: true); // keep test mode for now

            _kinectSvc = new KinectService();
            _cameraSvc = new CanonCameraService(exports);
            _thermalSvc = new ThermalScannerService();
            _intelSvc = new IntelIntellisenseService();
            _laserSvc = new LaserArrayService();

            // Session manager (kept)
            _sessionManager = new ScanSessionManager(barcode, scale, _kinectSvc, _cameraSvc);
            _sessionManager.OnNewScanResult += SessionManager_OnNewScanResult;
            _sessionManager.OnStatusMessage += SessionManager_OnStatusMessage;

            // Bind results
            _results = new ObservableCollection<ScanResult>();
            SessionDataGrid.ItemsSource = _results;
            ThumbnailBar.ItemsSource = _results;

            StatusTextBlock.Text = "Ready.";
        }

        private void SessionManager_OnStatusMessage(string message)
        {
            Dispatcher.Invoke(() => StatusTextBlock.Text = message);
        }

        public void TestDryRun()
        {
            Console.WriteLine("Dry run test success.");
        }

        private void NewButton_Click(object sender, RoutedEventArgs e)
        {
            string part = PartNumberTextBox.Text?.Trim();

            if (string.IsNullOrEmpty(part))
            {
                MessageBox.Show("Please enter a Part Number.",
                                "Warning",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                return;
            }

            _sessionManager.StartNewSession(part);
            _results.Clear();
            PreviewImage.Source = null;
            PreviewMetaTextBlock.Text = "";
            StatusTextBlock.Text = $"Started session for part {part}.";

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
                    if (pd.WidthIn.HasValue)  r.DepthIn = pd.WidthIn.Value;
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
                // Update preview
                string fullPath = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "Exports",
                    sel.ImageFileName);

                if (File.Exists(fullPath))
                {
                    try
                    {
                        PreviewImage.Source = new BitmapImage(new Uri(fullPath));
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
            pd.WidthIn  = TryParseDouble(WidthTextBox.Text);
            pd.HeightIn = TryParseDouble(HeightTextBox.Text);
            pd.Notes = NotesTextBox.Text ?? "";

            // Apply to all existing scan results for this part
            foreach (var row in _results.Where(r => string.Equals(r.PartNumber, part, StringComparison.OrdinalIgnoreCase)))
            {
                if (pd.LengthIn.HasValue) row.LengthIn = pd.LengthIn.Value;
                if (pd.WidthIn.HasValue)  row.DepthIn = pd.WidthIn.Value; // Width -> DepthIn
                if (pd.HeightIn.HasValue) row.HeightIn = pd.HeightIn.Value;
                if (pd.WeightLb.HasValue) row.WeightLb = pd.WeightLb.Value;
            }

            StatusTextBlock.Text = $"Applied dims/weight to part {part}.";
        }

        private void CaptureWeightButton_Click(object sender, RoutedEventArgs e)
        {
            // Keeping this compile-safe for now.
            // Next step: wire this to your ScaleService API so it reads once on click and returns lbs.
            StatusTextBlock.Text = "Capture Weight clicked (scale wiring next).";
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
                FileName = $"EasySnap_Export_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
            };

            if (sfd.ShowDialog() != true)
                return;

            try
            {
                // ONE ROW PER PART NUMBER
                CsvWriter.ExportOneRowPerPart(
                    sfd.FileName,
                    _results,
                    dimUnit: "in",
                    volUnit: "",
                    factor: "166",
                    siteId: "733"
                );

                StatusTextBlock.Text = $"Exported CSV: {Path.GetFileName(sfd.FileName)}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Export failed:\n{ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusTextBlock.Text = "Export failed.";
            }
        }

        private void LoadPartDataIntoPane(string part)
        {
            if (_partData.TryGetValue(part, out var pd))
            {
                WeightTextBox.Text = pd.WeightLb?.ToString() ?? "";
                LengthTextBox.Text = pd.LengthIn?.ToString() ?? "";
                WidthTextBox.Text  = pd.WidthIn?.ToString() ?? "";
                HeightTextBox.Text = pd.HeightIn?.ToString() ?? "";
                NotesTextBox.Text  = pd.Notes ?? "";
            }
            else
            {
                WeightTextBox.Text = "";
                LengthTextBox.Text = "";
                WidthTextBox.Text  = "";
                HeightTextBox.Text = "";
                NotesTextBox.Text  = "";
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

        private void MeasurementTools_Click(object sender, RoutedEventArgs e)
        {
            StatusTextBlock.Text = "Measurement Tools menu opened (not yet implemented)";
            // TODO: Open measurement tools window/dialog
        }

        private void PhotographyTools_Click(object sender, RoutedEventArgs e)
        {
            StatusTextBlock.Text = "Photography Tools menu opened (not yet implemented)";
            // TODO: Open photography tools window/dialog
        }

        private void OpenSettings_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new DeviceSettingsWindow(
                _kinectSvc,
                _cameraSvc,
                _thermalSvc,
                _intelSvc,
                _laserSvc)
            { Owner = this };

            dlg.ShowDialog();
        }
    }
}