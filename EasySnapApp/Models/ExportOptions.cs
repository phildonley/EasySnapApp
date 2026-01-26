using System;

namespace EasySnapApp.Models
{
    public enum ExportSizeMode
    {
        Original,
        LongEdge,
        FitInside
    }

    public class ExportOptions
    {
        public string OutputFolder { get; set; }
        public ExportSizeMode SizeMode { get; set; } = ExportSizeMode.Original;
        public int LongEdgePixels { get; set; } = 1920;
        public int FitWidth { get; set; } = 1920;
        public int FitHeight { get; set; } = 1080;
        public int JpegQuality { get; set; } = 85;
        public bool CreateZip { get; set; } = false;
        public bool IncludeManifest { get; set; } = true;
    }
}
