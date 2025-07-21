using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using EasySnapApp.Services;

namespace EasySnapApp.Views
{
    public partial class CalibrationDialog : Window
    {
        private readonly KinectService _kinectService;
        private WriteableBitmap _depthBitmap;

        public CalibrationDialog(KinectService kinectService)
        {
            InitializeComponent();
            _kinectService = kinectService;

            // load any saved real dims
            if (_kinectService.CurrentRealLength > 0)
                RealLengthBox.Text = _kinectService.CurrentRealLength.ToString("F4");
            if (_kinectService.CurrentRealWidth > 0)
                RealWidthBox.Text = _kinectService.CurrentRealWidth.ToString("F4");
            if (_kinectService.CurrentRealHeight > 0)
                RealHeightBox.Text = _kinectService.CurrentRealHeight.ToString("F4");

            // hook depth frames
            _kinectService.DepthReady += KinectService_DepthReady;

            // wire buttons
            CaptureBackgroundButton.Click += CaptureBackgroundButton_Click;
            MeasureBoxButton.Click += MeasureBoxButton_Click;
        }

        private void KinectService_DepthReady(object sender, DepthFrameEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                // 1) On first frame, create the WriteableBitmap
                if (_depthBitmap == null)
                {
                    _depthBitmap = new WriteableBitmap(
                        e.Width, e.Height, 96, 96, PixelFormats.Gray8, null);

                    // size the preview container & overlay to EXACT frame size
                    CalibrationPreviewContainer.Width = e.Width;
                    CalibrationPreviewContainer.Height = e.Height;
                    CalibrationOverlayCanvas.Width = e.Width;
                    CalibrationOverlayCanvas.Height = e.Height;

                    CalibrationDepthImage.Source = _depthBitmap;
                }

                // 2) Copy depth → 8-bit
                var pixels = new byte[e.Width * e.Height];
                for (int i = 0; i < pixels.Length; i++)
                    pixels[i] = (byte)(e.DepthData[i] >> 5);

                _depthBitmap.WritePixels(
                    new Int32Rect(0, 0, e.Width, e.Height),
                    pixels, e.Width, 0);

                // 3) Draw last measured box
                CalibrationBoxOverlay.Visibility = Visibility.Collapsed;
                var (minX, minY, maxX, maxY) = _kinectService.LastPixelBox;
                int w = Math.Max(0, maxX - minX);
                int h = Math.Max(0, maxY - minY);
                if (w > 0 && h > 0)
                {
                    CalibrationBoxOverlay.Width = w;
                    CalibrationBoxOverlay.Height = h;
                    Canvas.SetLeft(CalibrationBoxOverlay, minX);
                    Canvas.SetTop(CalibrationBoxOverlay, minY);
                    CalibrationBoxOverlay.Visibility = Visibility.Visible;
                }
            });
        }

        private void CaptureBackgroundButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _kinectService.CaptureBackground();
                MessageBox.Show("Background captured.", "Calibration",
                                MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error capturing background:\n{ex.Message}",
                                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void MeasureBoxButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var (L, W, H) = _kinectService.GetBoundingBox();
                MeasuredLabel.Text =
                    $"Measured: L={L:F2} in, W={W:F2} in, H={H:F2} in";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error measuring box:\n{ex.Message}",
                                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OKButton_Click(object sender, RoutedEventArgs e)
        {
            if (!double.TryParse(RealLengthBox.Text, out var realL) ||
                !double.TryParse(RealWidthBox.Text, out var realW) ||
                !double.TryParse(RealHeightBox.Text, out var realH))
            {
                MessageBox.Show("Please enter valid dimensions.",
                                "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                _kinectService.CalibrateWithBox(realL, realW, realH);
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Calibration failed:\n{ex.Message}",
                                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
