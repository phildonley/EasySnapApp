using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;               // for Debug.WriteLine
using Microsoft.Kinect;
using Newtonsoft.Json;
using EasySnapApp.Models;

namespace EasySnapApp.Services
{
    /// <summary>
    /// Carries raw depth data out of the Kinect for preview.
    /// </summary>
    public class DepthFrameEventArgs : EventArgs
    {
        public int Width { get; init; }
        public int Height { get; init; }
        public ushort[] DepthData { get; init; }
    }

    /// <summary>
    /// Wraps Kinect v2 DepthFrameReader + CoordinateMapper.
    /// Supports background subtraction, RANSAC plane‐fit, blob segmentation,
    /// calibration, and robust bounding‐box measurement.
    /// </summary>
    public class KinectService : IDisposable
    {
        // sensor, reader, mapper
        private readonly KinectSensor _sensor;
        private readonly DepthFrameReader _reader;
        private readonly CoordinateMapper _mapper;

        // raw buffers
        private readonly CameraSpacePoint[] _cameraPoints;
        private readonly ushort[] _lastRawDepth;
        private ushort[] _backgroundDepth;

        // frame dimensions
        private readonly int _depthWidth;
        private readonly int _depthHeight;

        // thresholds & RANSAC
        private const int DEPTH_THRESHOLD = 2;         // mm
        private const int RANSAC_ITERS = 500;
        private const float RANSAC_THRESHOLD = 0.010f; // meters

        // object-height limits (to reject noise far above/below stage)
        private const double MIN_OBJECT_HEIGHT = 0.010; // 10 mm above table
        private const double MAX_OBJECT_HEIGHT = 0.600; // 600 mm above table - a little under 24"

        // calibration persistence
        private CalibrationData _calibration = new CalibrationData();
        private const string CALIB_FILE = "calibration.json";
        private const string STAGE_BOUNDS_FILE = "stage_bounds.json";

        // stage crop in pixel coords
        private int _stageLeft = 0,
                    _stageTop = 0,
                    _stageRight = 511,
                    _stageBottom = 423;  // Kinect v2 default 512×424

        /// <summary>
        /// Fired whenever a new depth frame is available.
        /// </summary>
        public event EventHandler<DepthFrameEventArgs> DepthReady;

        /// <summary>
        /// The most recent blob pixel‐bounds: (MinX,MinY,MaxX,MaxY)
        /// </summary>
        public (int MinX, int MinY, int MaxX, int MaxY) LastPixelBox { get; private set; }

        public CameraSpacePoint[] LastCameraPoints => _cameraPoints;
        public CoordinateMapper Mapper => _mapper;

        public int StageLeft => _stageLeft;
        public int StageTop => _stageTop;
        public int StageRight => _stageRight;
        public int StageBottom => _stageBottom;

        public double CurrentRealLength => _calibration.RealLengthIn;
        public double CurrentRealWidth => _calibration.RealWidthIn;
        public double CurrentRealHeight => _calibration.RealHeightIn;

        /// <summary>
        /// True once the Kinect sensor has been opened successfully.
        /// </summary>
        public bool IsConnected => _sensor != null && _sensor.IsOpen;

        public KinectService()
        {
            Debug.WriteLine("[KinectService] Initializing…");

            _sensor = KinectSensor.GetDefault()
                   ?? throw new InvalidOperationException("No Kinect sensor found.");
            _reader = _sensor.DepthFrameSource.OpenReader();
            _mapper = _sensor.CoordinateMapper;

            var desc = _sensor.DepthFrameSource.FrameDescription;
            _depthWidth = desc.Width;
            _depthHeight = desc.Height;
            Debug.WriteLine($"[KinectService] Depth frame: {_depthWidth}×{_depthHeight}");

            _lastRawDepth = new ushort[desc.LengthInPixels];
            _cameraPoints = new CameraSpacePoint[desc.LengthInPixels];

            _reader.FrameArrived += OnDepthArrived;
            _sensor.Open();

            LoadCalibration();
            LoadStageBounds();
            Debug.WriteLine("[KinectService] Ready.");
        }

        #region Stage‐Bounds I/O

        public void SaveStageBounds()
        {
            Debug.WriteLine($"[StageBounds] Saving L={_stageLeft},T={_stageTop},R={_stageRight},B={_stageBottom}");
            var b = new StageBounds
            {
                Left = _stageLeft,
                Top = _stageTop,
                Right = _stageRight,
                Bottom = _stageBottom
            };
            File.WriteAllText(STAGE_BOUNDS_FILE,
                JsonConvert.SerializeObject(b, Formatting.Indented));
        }

        public void LoadStageBounds()
        {
            if (!File.Exists(STAGE_BOUNDS_FILE))
            {
                Debug.WriteLine("[StageBounds] No file, using defaults.");
                return;
            }
            try
            {
                var b = JsonConvert.DeserializeObject<StageBounds>(
                          File.ReadAllText(STAGE_BOUNDS_FILE))
                        ?? throw new InvalidOperationException("Corrupt bounds file.");
                _stageLeft = b.Left;
                _stageTop = b.Top;
                _stageRight = b.Right;
                _stageBottom = b.Bottom;
                Debug.WriteLine($"[StageBounds] Loaded L={_stageLeft},T={_stageTop},R={_stageRight},B={_stageBottom}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[StageBounds] Load failed: {ex}");
            }
        }

        public void SetStageBoundary(int left, int top, int right, int bottom)
        {
            Debug.WriteLine($"[StageBounds] Set L={left},T={top},R={right},B={bottom}");
            _stageLeft = left;
            _stageTop = top;
            _stageRight = right;
            _stageBottom = bottom;
        }

        public class StageBounds
        {
            public int Left { get; set; }
            public int Top { get; set; }
            public int Right { get; set; }
            public int Bottom { get; set; }
        }

        #endregion

        #region Frame Arrival & Background

        private void OnDepthArrived(object sender, DepthFrameArrivedEventArgs e)
        {
            using var frame = e.FrameReference.AcquireFrame();
            if (frame == null) return;

            frame.CopyFrameDataToArray(_lastRawDepth);
            _mapper.MapDepthFrameToCameraSpace(_lastRawDepth, _cameraPoints);

            DepthReady?.Invoke(this, new DepthFrameEventArgs
            {
                Width = frame.FrameDescription.Width,
                Height = frame.FrameDescription.Height,
                DepthData = (ushort[])_lastRawDepth.Clone()
            });
        }

        public void CaptureBackground()
        {
            if (_lastRawDepth == null || _lastRawDepth.Length == 0)
                throw new InvalidOperationException("No depth frame yet.");
            _backgroundDepth = (ushort[])_lastRawDepth.Clone();
            Debug.WriteLine("[Background] Captured.");
        }

        #endregion

        #region Plane‐Fit (RANSAC)

        private (double a, double b, double c, double d)
            FitPlaneRansac(CameraSpacePoint[] pool)
        {
            var rand = new Random();
            int bestInliers = 0;
            (double a, double b, double c, double d) bestPlane = (0, 0, 1, 0);

            for (int iter = 0; iter < RANSAC_ITERS; iter++)
            {
                CameraSpacePoint p1, p2, p3;
                do
                {
                    p1 = pool[rand.Next(pool.Length)];
                    p2 = pool[rand.Next(pool.Length)];
                    p3 = pool[rand.Next(pool.Length)];
                } while (AreCollinear(p1, p2, p3));

                var v1 = Sub(p2, p1);
                var v2 = Sub(p3, p1);
                var norm = Cross(v1, v2);
                double mag = Math.Sqrt(norm.X * norm.X + norm.Y * norm.Y + norm.Z * norm.Z);
                if (mag < 1e-6) continue;

                norm = new CameraSpacePoint
                {
                    X = (float)(norm.X / mag),
                    Y = (float)(norm.Y / mag),
                    Z = (float)(norm.Z / mag)
                };
                double d = -(norm.X * p1.X + norm.Y * p1.Y + norm.Z * p1.Z);

                int inliers = pool.Count(pt =>
                    Math.Abs(norm.X * pt.X + norm.Y * pt.Y + norm.Z * pt.Z + d)
                    < RANSAC_THRESHOLD);
                if (inliers > bestInliers)
                {
                    bestInliers = inliers;
                    bestPlane = (norm.X, norm.Y, norm.Z, d);
                }
            }

            Debug.WriteLine($"[PlaneFit] Best inliers: {bestInliers}/{pool.Length}");
            return bestPlane;
        }

        private static bool AreCollinear(CameraSpacePoint p1, CameraSpacePoint p2, CameraSpacePoint p3)
        {
            var c = Cross(Sub(p2, p1), Sub(p3, p1));
            return (c.X * c.X + c.Y * c.Y + c.Z * c.Z) < 1e-6;
        }

        private static CameraSpacePoint Sub(CameraSpacePoint a, CameraSpacePoint b)
            => new() { X = a.X - b.X, Y = a.Y - b.Y, Z = a.Z - b.Z };

        private static CameraSpacePoint Cross(CameraSpacePoint a, CameraSpacePoint b)
            => new()
            {
                X = a.Y * b.Z - a.Z * b.Y,
                Y = a.Z * b.X - a.X * b.Z,
                Z = a.X * b.Y - a.Y * b.X
            };

        #endregion

        #region Segmentation Helpers

        public bool[] GetForegroundMask()
        {
            var mask = new bool[_lastRawDepth.Length];
            if (_backgroundDepth == null)
            {
                Debug.WriteLine("[Mask] No background yet.");
                return mask;
            }

            for (int i = 0; i < mask.Length; i++)
            {
                var delta = Math.Abs(_lastRawDepth[i] - _backgroundDepth[i]);
                bool fg = delta > DEPTH_THRESHOLD
                       && _cameraPoints[i].Z > 0
                       && !float.IsInfinity(_cameraPoints[i].Z)
                       && !float.IsNaN(_cameraPoints[i].Z)
                       && IsInStageBoundary(i);
                mask[i] = fg;
            }

            return mask;
        }

        private bool[] KeepLargestBlob(bool[] mask)
        {
            int w = _depthWidth, h = _depthHeight;
            var labels = new int[mask.Length];
            var sizes = new Dictionary<int, int>();
            var q = new Queue<int>();
            int[,] dir = { { 1, 0 }, { -1, 0 }, { 0, 1 }, { 0, -1 } };
            int lab = 1;

            for (int i = 0; i < mask.Length; i++)
            {
                if (!mask[i] || labels[i] != 0) continue;
                labels[i] = lab;
                q.Enqueue(i);
                int count = 0;

                while (q.Count > 0)
                {
                    int idx = q.Dequeue(); count++;
                    int x = idx % w, y = idx / w;
                    for (int k = 0; k < 4; k++)
                    {
                        int nx = x + dir[k, 0], ny = y + dir[k, 1];
                        if (nx < 0 || nx >= w || ny < 0 || ny >= h) continue;
                        int nidx = ny * w + nx;
                        if (mask[nidx] && labels[nidx] == 0)
                        {
                            labels[nidx] = lab;
                            q.Enqueue(nidx);
                        }
                    }
                }

                sizes[lab++] = count;
            }

            if (sizes.Count == 0) return mask;
            int best = sizes.Aggregate((a, b) => a.Value > b.Value ? a : b).Key;
            var outM = new bool[mask.Length];
            for (int i = 0; i < mask.Length; i++)
                outM[i] = (labels[i] == best);
            return outM;
        }

        #endregion

        #region Bounding‐Box & Height

        /// <summary>
        /// Measure the length (X), depth (Y) and height above plane (Z) in inches.
        /// </summary>
        public (double L, double D, double H) GetBoundingBox()
        {
            if (_backgroundDepth == null)
                throw new InvalidOperationException("Background not captured.");

            // 1) Fit stage plane
            var bgPts = _cameraPoints
                       .Where((pt, i) => _backgroundDepth[i] > 0 && IsInStageBoundary(i))
                       .ToArray();
            var (a, b, c, d) = FitPlaneRansac(bgPts);

            // 2) Mask & denoise
            var rawMask = GetForegroundMask();
            Debug.WriteLine($"[DBG] rawMask count: {rawMask.Count(b => b)} of {rawMask.Length}");
            rawMask = DenoiseVerticalLines(rawMask, _depthWidth, _depthHeight);
            var mask = KeepLargestBlob(rawMask);
            int fgCount = mask.Count(m => m);
            Debug.WriteLine($"[DBG] KeepLargestBlob count: {fgCount}");

            if (fgCount < 10)
            {
                Debug.WriteLine("[DBG] Blob too small—skipping");
                return (0, 0, 0);
            }

            // 3) Gather blob pts
            var pts = mask
                  .Select((m, i) => (m, i))
                  .Where(x => x.m)
                  .Select(x => _cameraPoints[x.i])
                  .ToArray();

            // 4) RAW extents (5–95 percentile)
            var xs = pts.Select(p => (double)p.X).OrderBy(x => x).ToArray();
            var ys = pts.Select(p => (double)p.Y).OrderBy(y => y).ToArray();
            double rawMinX = Percentile(xs, 5), rawMaxX = Percentile(xs, 95);
            double rawMinY = Percentile(ys, 5), rawMaxY = Percentile(ys, 95);

            // 5) RAW height above plane
            var heights = pts.Select(p => {
                double zPlane = (-d - a * p.X - b * p.Y) / c;
                return p.Z - zPlane;
            }).ToArray();
            double rawMaxH = heights.Max();

            // 6) M→in
            const double M2IN = 39.3701;
            double rawW = (rawMaxX - rawMinX) * M2IN;
            double rawD = (rawMaxY - rawMinY) * M2IN;
            double rawH = rawMaxH * M2IN;

            // 7) scale
            double length = rawW * _calibration.ScaleLength;
            double depth = rawD * _calibration.ScaleWidth;
            double height = rawH * _calibration.ScaleHeight;

            // 8) pixel‐bounds
            var pixXs = mask
                   .Select((m, i) => (m, i))
                   .Where(x => x.m)
                   .Select(x => x.i % _depthWidth)
                   .ToArray();
            var pixYs = mask
                   .Select((m, i) => (m, i))
                   .Where(x => x.m)
                   .Select(x => x.i / _depthWidth)
                   .ToArray();

            LastPixelBox = (
               pixXs.Min(),
               pixYs.Min(),
               pixXs.Max(),
               pixYs.Max()
            );

            Debug.WriteLine(
              $"[BoundingBox] PixelBox: X={LastPixelBox.MinX}…{LastPixelBox.MaxX}, " +
              $"Y={LastPixelBox.MinY}…{LastPixelBox.MaxY}\n" +
              $"[BoundingBox] RAW (in): W={rawW:F2}, D={rawD:F2}, H={rawH:F2}\n" +
              $"[BoundingBox] Final:   L={length:F2}, D={depth:F2}, H={height:F2}"
            );

            return (length, depth, height);
        }

        #endregion

        #region Calibration

        /// <summary>
        /// Calibrate the inch‐scale using a box of known real dimensions.
        /// Returns the **measured** raw dims (inches) before scaling.
        /// </summary>
        public (double LengthIn, double WidthIn, double HeightIn)
            CalibrateWithBox(double realL, double realW, double realH)
        {
            if (_backgroundDepth == null)
                throw new InvalidOperationException("Background not captured.");

            Debug.WriteLine($"[Calibration] Real dims: L={realL}, W={realW}, H={realH}");

            // 1) Fit stage plane
            var ptsPlane = _cameraPoints
                .Where((pt, i) => _backgroundDepth[i] > 0 && IsInStageBoundary(i))
                .ToArray();
            var (a, b, c, d) = FitPlaneRansac(ptsPlane);

            // 2) Mask 10–100 mm above table
            var boxMask = new bool[_lastRawDepth.Length];
            for (int i = 0; i < boxMask.Length; i++)
            {
                var p = _cameraPoints[i];
                if (!IsInStageBoundary(i) || p.Z <= 0) continue;
                double zP = (-d - a * p.X - b * p.Y) / c;
                double dz = p.Z - zP;
                if (dz >= MIN_OBJECT_HEIGHT && dz <= MAX_OBJECT_HEIGHT)
                    boxMask[i] = true;
            }
            boxMask = KeepLargestBlob(boxMask);
            int cnt = boxMask.Count(b => b);
            Debug.WriteLine($"[Calibration] Box points: {cnt}");
            if (cnt < 100)
                throw new InvalidOperationException("Too few box points.");

            var pts = boxMask
                .Select((b, i) => (b, i))
                .Where(x => x.b)
                .Select(x => _cameraPoints[x.i])
                .ToArray();

            // 3) Raw extents, percentile trim 2–98%
            var xsRaw = pts.Select(p => (double)p.X).OrderBy(x => x).ToArray();
            var ysRaw = pts.Select(p => (double)p.Y).OrderBy(y => y).ToArray();
            double minX = Percentile(xsRaw, 2), maxX = Percentile(xsRaw, 98);
            double minY = Percentile(ysRaw, 2), maxY = Percentile(ysRaw, 98);

            // 4) Box height
            double boxH = pts.Select(p => {
                double zp = (-d - a * p.X - b * p.Y) / c;
                return p.Z - zp;
            }).Max();

            // 5) to inches
            const double M2IN = 39.3701;
            double measuredL = (maxX - minX) * M2IN;
            double measuredW = (maxY - minY) * M2IN;
            double measuredH = boxH * M2IN;

            Debug.WriteLine(
              $"[Calibration] Measured (in): L={measuredL:F2}, W={measuredW:F2}, H={measuredH:F2}");

            // 6) store
            _calibration.ScaleLength = realL > 0 ? realL / measuredL : 1;
            _calibration.ScaleWidth = realW > 0 ? realW / measuredW : 1;
            _calibration.ScaleHeight = realH > 0 ? realH / measuredH : 1;

            _calibration.RealLengthIn = realL;
            _calibration.RealWidthIn = realW;
            _calibration.RealHeightIn = realH;
            _calibration.LastCalibrated = DateTime.Now;
            SaveCalibration();

            Debug.WriteLine(
              $"[Calibration] Updated scales: SL={_calibration.ScaleLength:F3}, " +
              $"SW={_calibration.ScaleWidth:F3}, SH={_calibration.ScaleHeight:F3}");

            return (measuredL, measuredW, measuredH);
        }

        private void SaveCalibration()
        {
            try
            {
                File.WriteAllText(CALIB_FILE,
                    JsonConvert.SerializeObject(_calibration, Formatting.Indented));
                Debug.WriteLine($"[Calibration] Saved to {CALIB_FILE}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Calibration] Save failed: {ex}");
            }
        }

        private void LoadCalibration()
        {
            if (!File.Exists(CALIB_FILE))
            {
                Debug.WriteLine("[Calibration] None found.");
                return;
            }
            try
            {
                _calibration = JsonConvert
                  .DeserializeObject<CalibrationData>(File.ReadAllText(CALIB_FILE))
                  ?? new CalibrationData();
                Debug.WriteLine(
                  $"[Calibration] Loaded real L={_calibration.RealLengthIn}, " +
                  $"W={_calibration.RealWidthIn}, H={_calibration.RealHeightIn}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Calibration] Load failed: {ex}");
            }
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Simple percentile interpolation (no C# 8 Index)
        /// </summary>
        private static double Percentile(double[] sorted, double p)
        {
            if (sorted.Length == 0) return 0;
            if (p <= 0) return sorted[0];
            if (p >= 100) return sorted[sorted.Length - 1];

            double pos = (sorted.Length - 1) * (p / 100.0);
            int idx = (int)pos;
            double frac = pos - idx;
            return (idx + 1 < sorted.Length)
               ? sorted[idx] * (1 - frac) + sorted[idx + 1] * frac
               : sorted[idx];
        }

        private bool IsInStageBoundary(int index)
        {
            int x = index % _depthWidth,
                y = index / _depthWidth;
            return x >= _stageLeft && x <= _stageRight
                && y >= _stageTop && y <= _stageBottom;
        }

        private bool[] DenoiseVerticalLines(bool[] mask, int width, int height)
        {
            var clean = new bool[mask.Length];
            for (int i = 0; i < mask.Length; i++)
            {
                if (!mask[i]) continue;
                int x = i % width, y = i / width, cnt = 0;
                for (int dy = -2; dy <= 2; dy++)
                {
                    int yy = y + dy;
                    if (yy < 0 || yy >= height) continue;
                    if (mask[yy * width + x]) cnt++;
                }
                if (cnt >= 3) clean[i] = true;
            }
            return clean;
        }

        #endregion

        public void Dispose()
        {
            _reader?.Dispose();
            if (_sensor != null && _sensor.IsOpen) _sensor.Close();
            Debug.WriteLine("[KinectService] Disposed.");
        }
    }
}
