using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using EasySnapApp.Models;
using EasySnapApp.Services;

namespace EasySnapApp.Views
{
    public partial class StageSettingsDialog : Window
    {
        private readonly KinectService _kinectService;
        private WriteableBitmap _latestDepthBitmap;
        public StageSettings Settings { get; private set; }

        private Ellipse _draggingCorner = null;
        private Point _dragOffset;

        public StageSettingsDialog(KinectService kinectService, StageSettings initialSettings = null)
        {
            InitializeComponent();
            _kinectService = kinectService;

            // Initialize with current or default settings
            Settings = initialSettings ?? new StageSettings
            {
                RectLeft = _kinectService.StageLeft,
                RectTop = _kinectService.StageTop,
                RectRight = _kinectService.StageRight,
                RectBottom = _kinectService.StageBottom
            };

            // Fill in the text boxes & label
            LeftBox.Text = Settings.RectLeft.ToString();
            TopBox.Text = Settings.RectTop.ToString();
            RightBox.Text = Settings.RectRight.ToString();
            BottomBox.Text = Settings.RectBottom.ToString();
            UpdateAreaLabel();

            // Restore bounding‐box mode
            switch (Settings.BoundingBoxMode)
            {
                case BoundingBoxDisplayMode.Live: LiveBoxRadio.IsChecked = true; break;
                case BoundingBoxDisplayMode.Preview: PreviewBoxRadio.IsChecked = true; break;
                case BoundingBoxDisplayMode.None: NoneBoxRadio.IsChecked = true; break;
            }
            PreviewSecondsBox.Text = Settings.PreviewDurationSeconds.ToString();

            // Subscribe to live depth
            _kinectService.DepthReady += KinectService_DepthReady;

            // Wire up UI events
            LeftBox.TextChanged += CropBox_TextChanged;
            TopBox.TextChanged += CropBox_TextChanged;
            RightBox.TextChanged += CropBox_TextChanged;
            BottomBox.TextChanged += CropBox_TextChanged;
            ResetButton.Click += (s, e) => ResetRect();

            LiveBoxRadio.Checked += (s, e) => UpdateBoundingBoxMode();
            PreviewBoxRadio.Checked += (s, e) => UpdateBoundingBoxMode();
            NoneBoxRadio.Checked += (s, e) => UpdateBoundingBoxMode();
            PreviewSecondsBox.TextChanged += (s, e) => UpdatePreviewSeconds();

            ThreshSlider.ValueChanged += (s, e) => ThreshValue.Text = ThreshSlider.Value.ToString("F0");

            OKButton.Click += OKButton_Click;
            CancelButton.Click += CancelButton_Click;

            // Draw the initial overlay (mask + red rect + corners)
            DrawOverlay();
        }

        private void UpdateAreaLabel()
        {
            int w = Settings.RectRight - Settings.RectLeft + 1;
            int h = Settings.RectBottom - Settings.RectTop + 1;
            AreaLabel.Text = $"Stage Area: {w} × {h} px";
        }

        private void CropBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (int.TryParse(LeftBox.Text, out var l)) Settings.RectLeft = l;
            if (int.TryParse(TopBox.Text, out var t)) Settings.RectTop = t;
            if (int.TryParse(RightBox.Text, out var r)) Settings.RectRight = r;
            if (int.TryParse(BottomBox.Text, out var b)) Settings.RectBottom = b;
            UpdateAreaLabel();
            DrawOverlay();
        }

        private void KinectService_DepthReady(object sender, DepthFrameEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                // 1) render depth → WriteableBitmap
                if (_latestDepthBitmap == null
                 || _latestDepthBitmap.PixelWidth != e.Width
                 || _latestDepthBitmap.PixelHeight != e.Height)
                {
                    _latestDepthBitmap = new WriteableBitmap(
                        e.Width, e.Height, 96, 96, PixelFormats.Gray8, null);
                }

                var pixels = new byte[e.Width * e.Height];
                for (int i = 0; i < pixels.Length; i++)
                    pixels[i] = (byte)(e.DepthData[i] >> 5);

                _latestDepthBitmap.WritePixels(
                    new Int32Rect(0, 0, e.Width, e.Height),
                    pixels, e.Width, 0);

                PreviewImage.Source = _latestDepthBitmap;

                // 2) redraw overlay
                DrawOverlay();
            });
        }

        private void DrawOverlay()
        {
            OverlayCanvas.Children.Clear();

            // 1) mask
            var mask = _kinectService.GetForegroundMask();
            int imgW = _latestDepthBitmap?.PixelWidth ?? 0;
            int imgH = _latestDepthBitmap?.PixelHeight ?? 0;
            if (mask.Length == imgW * imgH && imgW > 0)
            {
                var pixels = new int[imgW * imgH];
                const int alpha = 0x40 << 24;
                const int green = 0xFF << 8;
                for (int i = 0; i < pixels.Length; i++)
                    pixels[i] = mask[i] ? (alpha | green) : 0;

                var maskBmp = new WriteableBitmap(
                    imgW, imgH, 96, 96, PixelFormats.Bgra32, null);
                maskBmp.WritePixels(
                    new Int32Rect(0, 0, imgW, imgH),
                    pixels, imgW * 4, 0);

                OverlayCanvas.Children.Add(new Image { Source = maskBmp });
            }

            // 2) crop rect + corners
            int w = Settings.RectRight - Settings.RectLeft + 1;
            int h = Settings.RectBottom - Settings.RectTop + 1;
            if (w > 0 && h > 0)
            {
                var rect = new Rectangle
                {
                    Width = w,
                    Height = h,
                    Stroke = Brushes.Red,
                    StrokeThickness = 2
                };
                Canvas.SetLeft(rect, Settings.RectLeft);
                Canvas.SetTop(rect, Settings.RectTop);
                OverlayCanvas.Children.Add(rect);

                void DrawCorner(int cx, int cy)
                {
                    var dot = new Ellipse
                    {
                        Width = 12,
                        Height = 12,
                        Fill = Brushes.Red,
                        Stroke = Brushes.White,
                        StrokeThickness = 2,
                        Cursor = Cursors.SizeAll
                    };
                    Canvas.SetLeft(dot, cx - 6);
                    Canvas.SetTop(dot, cy - 6);
                    dot.MouseLeftButtonDown += Corner_MouseLeftButtonDown;
                    dot.MouseMove += Corner_MouseMove;
                    dot.MouseLeftButtonUp += Corner_MouseLeftButtonUp;
                    OverlayCanvas.Children.Add(dot);
                }

                DrawCorner(Settings.RectLeft, Settings.RectTop);
                DrawCorner(Settings.RectRight, Settings.RectTop);
                DrawCorner(Settings.RectLeft, Settings.RectBottom);
                DrawCorner(Settings.RectRight, Settings.RectBottom);
            }
        }

        private void Corner_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _draggingCorner = (Ellipse)sender;
            _dragOffset = e.GetPosition(OverlayCanvas);
            _draggingCorner.CaptureMouse();
        }

        private void Corner_MouseMove(object sender, MouseEventArgs e)
        {
            if (_draggingCorner == null || e.LeftButton != MouseButtonState.Pressed)
                return;

            var pos = e.GetPosition(OverlayCanvas);
            int dx = (int)pos.X, dy = (int)pos.Y;
            int[] xs = { Settings.RectLeft, Settings.RectRight };
            int[] ys = { Settings.RectTop, Settings.RectBottom };

            int closest = 0, best = int.MaxValue;
            for (int i = 0; i < 4; i++)
            {
                int cx = xs[i % 2], cy = ys[i / 2];
                int dist = (cx - dx) * (cx - dx) + (cy - dy) * (cy - dy);
                if (dist < best) { best = dist; closest = i; }
            }
            switch (closest)
            {
                case 0: Settings.RectLeft = dx; Settings.RectTop = dy; break;
                case 1: Settings.RectRight = dx; Settings.RectTop = dy; break;
                case 2: Settings.RectLeft = dx; Settings.RectBottom = dy; break;
                case 3: Settings.RectRight = dx; Settings.RectBottom = dy; break;
            }
            LeftBox.Text = Settings.RectLeft.ToString();
            TopBox.Text = Settings.RectTop.ToString();
            RightBox.Text = Settings.RectRight.ToString();
            BottomBox.Text = Settings.RectBottom.ToString();
            UpdateAreaLabel();
            DrawOverlay();
        }

        private void Corner_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_draggingCorner != null)
            {
                _draggingCorner.ReleaseMouseCapture();
                _draggingCorner = null;
            }
        }

        private void ResetRect()
        {
            Settings.RectLeft = 50;
            Settings.RectTop = 50;
            Settings.RectRight = 430;
            Settings.RectBottom = 330;
            LeftBox.Text = Settings.RectLeft.ToString();
            TopBox.Text = Settings.RectTop.ToString();
            RightBox.Text = Settings.RectRight.ToString();
            BottomBox.Text = Settings.RectBottom.ToString();
            UpdateAreaLabel();
            DrawOverlay();
        }

        private void UpdateBoundingBoxMode()
        {
            if (LiveBoxRadio.IsChecked == true) Settings.BoundingBoxMode = BoundingBoxDisplayMode.Live;
            else if (PreviewBoxRadio.IsChecked == true) Settings.BoundingBoxMode = BoundingBoxDisplayMode.Preview;
            else Settings.BoundingBoxMode = BoundingBoxDisplayMode.None;
        }

        private void UpdatePreviewSeconds()
        {
            if (int.TryParse(PreviewSecondsBox.Text, out var s) && s > 0)
                Settings.PreviewDurationSeconds = s;
        }

        private void OKButton_Click(object sender, RoutedEventArgs e)
        {
            _kinectService.SetStageBoundary(
                Settings.RectLeft,
                Settings.RectTop,
                Settings.RectRight,
                Settings.RectBottom);
            _kinectService.SaveStageBounds();
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
