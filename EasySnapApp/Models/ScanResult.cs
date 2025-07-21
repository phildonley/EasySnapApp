// Models/ScanResult.cs
using System;
using System.Windows.Media.Imaging;

namespace EasySnapApp.Models
{
    public class ScanResult
    {
        public string PartNumber { get; set; }
        public int Sequence { get; set; }
        public string ImageFileName { get; set; }
        public string TimeStamp { get; set; }
        public double LengthIn { get; set; }
        public double DepthIn { get; set; }
        public double HeightIn { get; set; }
        public double WeightLb { get; set; }
        public BitmapImage ThumbnailImage { get; set; }
        public string TooltipText { get; set; }
        public bool IsSelected { get; set; }

        // Convenience property for data grid display, e.g. "5.12×3.50×1.80"
        public string Dims => $"{LengthIn:F2}×{DepthIn:F2}×{HeightIn:F2}";
    }
}
