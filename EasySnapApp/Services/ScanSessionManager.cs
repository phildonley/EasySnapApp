using System;
using System.Collections.Generic;
using System.IO;
using EasySnapApp.Models;
using EasySnapApp.Utils;    // for CsvWriter

namespace EasySnapApp.Services
{
    public class ScanSessionManager
    {
        private readonly BarcodeScannerService _barcode;
        private readonly ScaleService _scale;
        private readonly KinectService _kinect;
        private readonly CanonCameraService _camera;

        private readonly List<ScanResult> _session = new();
        private int _sequence;
        private bool _gotData;
        private string _partNumber = "";

        public event Action<ScanResult> OnNewScanResult;
        public event Action<string> OnStatusMessage;

        public ScanSessionManager(
            BarcodeScannerService barcode,
            ScaleService scale,
            KinectService kinect,
            CanonCameraService camera)
        {
            _barcode = barcode;
            _scale = scale;
            _kinect = kinect;
            _camera = camera;
        }

        public void StartNewSession(string partNumber)
        {
            _partNumber = partNumber;
            _session.Clear();
            _sequence = 103;
            _gotData = false;
            OnStatusMessage?.Invoke($"Session started for {partNumber}");
        }

        public void Capture()
        {
            if (string.IsNullOrEmpty(_partNumber))
            {
                OnStatusMessage?.Invoke("⚠️ No part number.");
                return;
            }

            double lengthIn = 0, depthIn = 0, heightIn = 0, weight = 0;
            if (!_gotData)
            {
                try
                {
                    (double Lm, double Dm, double Hm) = _kinect.GetBoundingBox();
                    lengthIn = Lm;
                    depthIn = Dm;
                    heightIn = Hm;
                    weight = _scale.GetWeight();
                    _gotData = true;
                }
                catch (Exception ex)
                {
                    OnStatusMessage?.Invoke($"❌ Data error: {ex.Message}");
                    return;
                }
            }

            string fullPath = _camera.CaptureImage(_partNumber);
            if (string.IsNullOrEmpty(fullPath))
            {
                OnStatusMessage?.Invoke("❌ Photo failed.");
                return;
            }

            var result = new ScanResult
            {
                PartNumber = _partNumber,
                Sequence = _sequence,
                ImageFileName = Path.GetFileName(fullPath),
                TimeStamp = DateTime.Now.ToString("yyyyMMdd_HHmmss"),
                LengthIn = lengthIn,
                DepthIn = depthIn,
                HeightIn = heightIn,
                WeightLb = weight
            };

            string csvFile = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "Exports",
                "results.csv"
            );
            CsvWriter.WriteScanResultToCsv(csvFile, result);

            _session.Add(result);
            OnNewScanResult?.Invoke(result);
            OnStatusMessage?.Invoke($"✅ Captured {result.ImageFileName}");
            _sequence += 2;
        }
    }
}
