using System;
using System.ComponentModel;
using System.Windows.Media.Imaging;
using EasySnapApp.Data; // For CapturedImage type

namespace EasySnapApp.Models
{
    /// <summary>
    /// ViewModel for captured images with UI binding support
    /// Phase 3.9: Enhanced with selection tracking and thumbnail support
    /// </summary>
    public class ImageRecordViewModel : INotifyPropertyChanged
    {
        private bool _isSelected;
        private BitmapImage _thumbnailImage;
        
        // Core data properties
        public string ImageId { get; set; }
        public string SessionId { get; set; }
        public string PartNumber { get; set; }
        public int Sequence { get; set; }
        public string SequenceDisplay
        {
            get
            {
                // Primary: derive from the actual filename already on disk.
                // This means the column always matches the real file name,
                // even if the user changes SequenceDigits/SequencePadding later.
                if (!string.IsNullOrEmpty(FileName))
                {
                    var stem = System.IO.Path.GetFileNameWithoutExtension(FileName);
                    // Strip .thumb suffix if somehow present
                    if (stem.EndsWith(".thumb", StringComparison.OrdinalIgnoreCase))
                        stem = stem.Substring(0, stem.Length - 6);
                    var lastDot = stem.LastIndexOf('.');
                    if (lastDot >= 0)
                    {
                        var seqPart = stem.Substring(lastDot + 1);
                        if (int.TryParse(seqPart, out _))
                            return seqPart; // Return exactly as it appears in the filename
                    }
                }

                // Fallback: no filename yet (new capture not yet saved).
                // Format using current settings so the preview is accurate.
                try
                {
                    var pad = Properties.Settings.Default.SequencePadding;
                    var digits = Properties.Settings.Default.SequenceDigits;
                    if (pad && digits > 0)
                        return Sequence.ToString(new string('0', digits));
                    return Sequence.ToString();
                }
                catch
                {
                    return Sequence.ToString();
                }
            }
        }
        public string FullPath { get; set; }
        public string ThumbPath { get; set; }
        public string FileName => System.IO.Path.GetFileName(FullPath);
        public DateTime CaptureTimeUtc { get; set; }
        public long FileSizeBytes { get; set; }
        
        // Measurement properties
        public double? WeightLb { get; set; }
        public double? LengthIn { get; set; }
        public double? DepthIn { get; set; } // Width mapped to DepthIn for existing compatibility
        public double? HeightIn { get; set; }
        
        // UI properties
        public bool IsSelected 
        { 
            get { return _isSelected; }
            set 
            { 
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged(nameof(IsSelected));
                }
            }
        }
        
        public BitmapImage ThumbnailImage
        {
            get { return _thumbnailImage; }
            set 
            {
                if (_thumbnailImage != value)
                {
                    _thumbnailImage = value;
                    OnPropertyChanged(nameof(ThumbnailImage));
                }
            }
        }

        // Display properties
        public string DisplayName => $"{PartNumber}_{SequenceDisplay}";
        public string FileSizeMB => $"{FileSizeBytes / (1024.0 * 1024.0):F2} MB";
        public string TimeStamp => CaptureTimeUtc.ToString("yyyyMMdd_HHmmss");
        public string ImageFileName => FileName;
        
        public event PropertyChangedEventHandler PropertyChanged;
        
        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        
        /// <summary>
        /// Create from existing ScanResult for compatibility
        /// </summary>
        public static ImageRecordViewModel FromScanResult(ScanResult scanResult)
        {
            return new ImageRecordViewModel
            {
                PartNumber = scanResult.PartNumber,
                Sequence = scanResult.Sequence,
                FullPath = scanResult.FullImagePath,
                ThumbPath = scanResult.ThumbnailPath,
                CaptureTimeUtc = DateTime.TryParse(scanResult.TimeStamp, out var dt) ? dt : DateTime.Now,
                FileSizeBytes = 0, // Will be populated from file info
                LengthIn = scanResult.LengthIn,
                DepthIn = scanResult.DepthIn,
                HeightIn = scanResult.HeightIn,
                WeightLb = scanResult.WeightLb,
                ThumbnailImage = scanResult.ThumbnailImage
            };
        }

        /// <summary>
        /// Create from database CapturedImage record - respects user's unit preferences
        /// </summary>
        public static ImageRecordViewModel FromCapturedImage(CapturedImage dbImage)
        {
            // Read user's preferred units from settings
            var dimUnit = Properties.Settings.Default.ExportDimUnit ?? "in";
            var wgtUnit = Properties.Settings.Default.ExportWgtUnit ?? "lb";

            return new ImageRecordViewModel
            {
                ImageId = dbImage.ImageId,
                SessionId = dbImage.SessionId,
                PartNumber = dbImage.PartNumber,
                Sequence = dbImage.Sequence,
                FullPath = dbImage.FullPath,
                ThumbPath = dbImage.ThumbPath,
                CaptureTimeUtc = dbImage.CaptureTimeUtc,
                FileSizeBytes = dbImage.FileSizeBytes,

                // Convert from database units (mm/grams) to user's preferred units
                LengthIn = ConvertDimensionFromDb(dbImage.DimX, dimUnit),
                DepthIn = ConvertDimensionFromDb(dbImage.DimY, dimUnit),
                HeightIn = ConvertDimensionFromDb(dbImage.DimZ, dimUnit),
                WeightLb = ConvertWeightFromDb(dbImage.WeightGrams, wgtUnit)
            };
        }

        /// <summary>
        /// Convert dimension from database (mm) to user's preferred unit
        /// </summary>
        private static double? ConvertDimensionFromDb(double? mmValue, string targetUnit)
        {
            if (!mmValue.HasValue) return null;

            var mm = mmValue.Value;

            return targetUnit.ToLowerInvariant() switch
            {
                "in" => mm / 25.4,           // mm to inches
                "cm" => mm / 10.0,           // mm to cm  
                "mm" => mm,                  // mm to mm (no conversion)
                _ => mm / 25.4               // default to inches
            };
        }

        /// <summary>
        /// Convert weight from database (grams) to user's preferred unit  
        /// </summary>
        private static double? ConvertWeightFromDb(double? gramsValue, string targetUnit)
        {
            if (!gramsValue.HasValue) return null;

            var grams = gramsValue.Value;

            return targetUnit.ToLowerInvariant() switch
            {
                "lb" => grams * 0.00220462,  // grams to pounds
                "kg" => grams / 1000.0,      // grams to kg
                "g" => grams,                // grams to grams (no conversion)
                _ => grams * 0.00220462      // default to pounds
            };
        }
    }
}
