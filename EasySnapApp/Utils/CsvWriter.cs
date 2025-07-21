using EasySnapApp.Models;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace EasySnapApp.Utils
{
    public static class CsvWriter
    {
        // Must match your CSV spec exactly:
        private static readonly string[] Headers = new[]
        {
            "ITEM_ID","LENGTH","WIDTH","HEIGHT","WEIGHT","VOLUME",
            "DIM_UNIT","WGT_UNIT","VOL_UNIT","FACTOR","SITE_ID",
            "TIME_STAMP","OPT_INFO_1","OPT_INFO_2","OPT_INFO_3",
            "OPT_INFO_4","OPT_INFO_5","OPT_INFO_6","OPT_INFO_7",
            "OPT_INFO_8","IMAGE_FILE_NAME","UPDATED"
        };

        /// <summary>
        /// Appends one row to the CSV, but only if there is a non-zero measurement.
        /// </summary>
        public static void WriteScanResultToCsv(string csvPath, ScanResult r)
        {
            // only export real measurements (skip 0,0,0)
            if (r.LengthIn <= 0 || r.DepthIn <= 0 || r.HeightIn <= 0)
                return;

            // ensure directory exists
            Directory.CreateDirectory(Path.GetDirectoryName(csvPath)!);

            // open for append (or create), allow others to read
            using var fs = new FileStream(
                csvPath,
                FileMode.OpenOrCreate,
                FileAccess.Write,
                FileShare.Read);

            using var w = new StreamWriter(fs, Encoding.UTF8);

            // if empty file, write header first
            if (fs.Length == 0)
                w.WriteLine(string.Join(",", Headers));

            // build the row
            var row = string.Join(",",
                r.PartNumber,                                    // ITEM_ID
                r.LengthIn.ToString("F2", CultureInfo.InvariantCulture), // LENGTH
                r.DepthIn.ToString("F2", CultureInfo.InvariantCulture), // WIDTH  <-- note: renamed
                r.HeightIn.ToString("F2", CultureInfo.InvariantCulture), // HEIGHT
                r.WeightLb.ToString("F2", CultureInfo.InvariantCulture), // WEIGHT
                "0",        // VOLUME (auto-filled zero)
                "in",       // DIM_UNIT
                "lb",       // WGT_UNIT
                "in",       // VOL_UNIT
                "166",      // FACTOR
                "733",      // SITE_ID
                r.TimeStamp,             // TIME_STAMP
                "",                      // OPT_INFO_1
                "Y",                     // OPT_INFO_2
                "", "", "", "", "", "",  // OPT_INFO_3…OPT_INFO_8
                r.ImageFileName,         // IMAGE_FILE_NAME
                "Y"                      // UPDATED
            );

            w.WriteLine(row);
        }
    }
}
