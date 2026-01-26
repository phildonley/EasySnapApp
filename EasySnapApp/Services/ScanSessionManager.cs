using System;
using System.Collections.Generic;
using System.IO;
using EasySnapApp.Models;

namespace EasySnapApp.Services
{
    public class ScanSessionManager
    {
        private readonly BarcodeScannerService _barcode;
        private readonly ScaleService _scale;

        private readonly CanonCameraService _camera;

        private readonly List<ScanResult> _session = new();
        private int _sequence;
        private string _partNumber = "";

        // Part-level data that must apply to every image in the part sequence
        private double _partLengthIn = 0;
        private double _partWidthIn = 0;   // maps to ScanResult.DepthIn for now
        private double _partHeightIn = 0;
        private double _partWeightLb = 0;

        public event Action<ScanResult> OnNewScanResult;
        public event Action<string> OnStatusMessage;

        public ScanSessionManager(
            BarcodeScannerService barcode,
            ScaleService scale,
            CanonCameraService camera)
        {
            _barcode = barcode;
            _scale = scale;
            _camera = camera;
        }

        public void StartNewSession(string partNumber)
        {
            _partNumber = partNumber?.Trim() ?? "";
            _session.Clear();

            // Always start at 103 and step +2 (future: move to settings)
            _sequence = 103;

            // Do NOT reset part-level measurements automatically — you can if you prefer.
            // For now, reset per new session so you don't accidentally reuse dims/weight.
            _partLengthIn = 0;
            _partWidthIn = 0;
            _partHeightIn = 0;
            _partWeightLb = 0;

            OnStatusMessage?.Invoke($"Session started for {_partNumber}");
        }

        /// <summary>
        /// Returns a snapshot of the current session results.
        /// </summary>
        public IReadOnlyList<ScanResult> GetSessionResults() => _session.AsReadOnly();

        /// <summary>
        /// Applies part-level measurements that should appear on every image row for the part.
        /// This updates existing rows and will also be used for future captures.
        /// </summary>
        public void ApplyPartMeasurements(double lengthIn, double widthIn, double heightIn, double weightLb)
        {
            _partLengthIn = lengthIn;
            _partWidthIn = widthIn;   // stored as width, mapped to DepthIn
            _partHeightIn = heightIn;
            _partWeightLb = weightLb;

            // Update existing rows in the current session
            foreach (var r in _session)
            {
                r.LengthIn = _partLengthIn;
                r.DepthIn = _partWidthIn; // Width -> DepthIn
                r.HeightIn = _partHeightIn;
                r.WeightLb = _partWeightLb;
            }

            OnStatusMessage?.Invoke($"Applied part measurements to {_partNumber}");
        }

        /// <summary>
        /// Captures a single weight reading from the scale and applies it to the whole part.
        /// Does NOT take a photo.
        /// </summary>
        public double CaptureWeightForCurrentPart()
        {
            if (string.IsNullOrWhiteSpace(_partNumber))
                throw new InvalidOperationException("No part number set.");

            var w = _scale.CaptureWeightLbOnce();
            _partWeightLb = w;

            // Update all existing rows for this part
            foreach (var r in _session)
                r.WeightLb = _partWeightLb;

            OnStatusMessage?.Invoke($"Captured weight {_partWeightLb:F2} lb for {_partNumber}");
            return _partWeightLb;
        }

        /// <summary>
        /// Capture a photo ONLY. No Kinect measurement, no scale reading, no auto CSV.
        /// Image filename/sequence is managed here.
        /// </summary>
        public void Capture()
        {
            if (string.IsNullOrWhiteSpace(_partNumber))
            {
                OnStatusMessage?.Invoke("⚠️ No part number.");
                return;
            }

            // Take photo
            string fullPath = _camera.CaptureImage(_partNumber);
            if (string.IsNullOrWhiteSpace(fullPath))
            {
                OnStatusMessage?.Invoke("❌ Photo failed.");
                return;
            }

            // Build result row (dims/weight are part-level and will show on every image)
            var result = new ScanResult
            {
                PartNumber = _partNumber,
                Sequence = _sequence,
                ImageFileName = Path.GetFileName(fullPath),
                TimeStamp = DateTime.Now.ToString("yyyyMMdd_HHmmss"),

                // Width maps to DepthIn for now (consistent with your existing model)
                LengthIn = _partLengthIn,
                DepthIn = _partWidthIn,
                HeightIn = _partHeightIn,
                WeightLb = _partWeightLb
            };

            _session.Add(result);
            OnNewScanResult?.Invoke(result);

            OnStatusMessage?.Invoke($"✅ Captured {result.ImageFileName}");
            _sequence += 2;
        }
    }
}