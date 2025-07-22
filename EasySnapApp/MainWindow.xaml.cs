using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Kinect;
using EasySnapApp.Models;
using EasySnapApp.Services;
using EasySnapApp.Views;
using Path = System.IO.Path;
using System.Windows.Media;

namespace EasySnapApp
{
    public partial class MainWindow : Window
    {
        private readonly ScanSessionManager _sessionManager;
        private readonly ObservableCollection<ScanResult> _results;
        private readonly KinectService _kinectSvc;
        private readonly CanonCameraService _cameraSvc;
        private readonly ThermalScannerService _thermalSvc;
        private readonly IntelIntellisenseService _intelSvc;
        private readonly LaserArrayService _laserSvc;

        public MainWindow()
        {
            InitializeComponent();

            // prepare exports folder
            var exports = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Exports");
            Directory.CreateDirectory(exports);

            // instantiate services
            var barcode = new BarcodeScannerService();
            var scale = new ScaleService(testMode: true);
            _kinectSvc = new KinectService();
            _cameraSvc = new CanonCameraService(exports);
            _thermalSvc = new ThermalScannerService();
            _intelSvc = new IntelIntellisenseService();
            _laserSvc = new LaserArrayService();

            // hook up live depth frames
            _kinectSvc.DepthReady += OnDepthReady;

            // session manager
            _sessionManager = new ScanSessionManager(barcode, scale, _kinectSvc, _cameraSvc);
            _sessionManager.OnNewScanResult += SessionManager_OnNewScanResult;
            _sessionManager.OnStatusMessage += SessionManager_OnStatusMessage;

            // bind results
            _results = new ObservableCollection<ScanResult>();
            SessionDataGrid.ItemsSource = _results;
            ThumbnailBar.ItemsSource = _results;
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
            DepthImage.Source = null;
            StatusTextBlock.Text = $"Started session for part {part}.";
        }

        private void OnDepthReady(object sender, DepthFrameEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                int left = _kinectSvc.StageLeft;
                int top = _kinectSvc.StageTop;
                int right = _kinectSvc.StageRight;
                int bottom = _kinectSvc.StageBottom;

                int cropW = right - left;
                int cropH = bottom - top;
                if (cropW <= 0 || cropH <= 0) return;

                var pixels = new byte[cropW * cropH];
                for (int y = 0; y < cropH; y++)
                {
                    int srcY = top + y;
                    if (srcY < 0 || srcY >= e.Height) continue;
                    for (int x = 0; x < cropW; x++)
                    {
                        int srcX = left + x;
                        if (srcX < 0 || srcX >= e.Width) continue;
                        int srcIdx = srcY * e.Width + srcX;
                        pixels[y * cropW + x] = (byte)(e.DepthData[srcIdx] >> 5);
                    }
                }

                var wb = new WriteableBitmap(cropW, cropH, 96, 96,
                                             PixelFormats.Gray8, null);
                wb.WritePixels(new Int32Rect(0, 0, cropW, cropH),
                               pixels, cropW, 0);
                DepthImage.Source = wb;
            });
        }

        private void OpenTestWindow_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Views.TestWindow() { Owner = this };
            dlg.ShowDialog();
        }

        private void SessionManager_OnNewScanResult(ScanResult r)
        {
            Dispatcher.Invoke(() => _results.Add(r));
        }

        private void CaptureButton_Click(object sender, RoutedEventArgs e)
        {
            _sessionManager.Capture();
        }

        private void SessionDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SessionDataGrid.SelectedItem is ScanResult sel)
            {
                string fullPath = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "Exports",
                    sel.ImageFileName);

                if (File.Exists(fullPath))
                {
                    try
                    {
                        PreviewImage.Source =
                            new BitmapImage(new Uri(fullPath));
                    }
                    catch { /* ignore invalid image */ }
                }
            }
        }

        private void Thumbnail_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is Image img && img.DataContext is ScanResult result)
            {
                SessionDataGrid.SelectedItem = result;
                SessionDataGrid.ScrollIntoView(result);
            }
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private void Calibrate3DCamera_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new CalibrationDialog(_kinectSvc) { Owner = this };
            if (dlg.ShowDialog() == true)
                Title = "EasySnap — 3D camera calibrated";
        }

        private void StageSetup_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new StageSettingsDialog(_kinectSvc) { Owner = this };
            dlg.ShowDialog();
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
