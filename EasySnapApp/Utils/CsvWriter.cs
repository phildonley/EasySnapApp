using EasySnapApp.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace EasySnapApp.Utils
{
    public static class CsvWriter
    {
        // IMPORTANT:
        // Export is ONE ROW PER PART NUMBER (not per image).
        // Headers must match your template schema exactly, including blank columns.
        //
        // Template schema (as provided earlier in your example):
        private static readonly string[] Headers = new[]
        {
            "ITEM_ID",
            "ITEM_TYPE",
            "DESCRIPTION",
            "NET_LENGTH",
            "NET_WIDTH",
            "NET_HEIGHT",
            "NET_WEIGHT",
            "NET_VOLUME",
            "NET_DIM_WEIGHT",
            "DIM_UNIT",
            "VOL_UNIT",
            "FACTOR",
            "SITE_ID",
            "TIME_STAMP",
            "OPT_INFO_1",
            "OPT_INFO_2",
            "OPT_INFO_3",
            "OPT_INFO_4",
            "OPT_INFO_5",
            "OPT_INFO_6",
            "OPT_INFO_7",
            "OPT_INFO_8",
            "IMAGE_FILE_NAME",
            "UPDATED"
        };

        /// <summary>
        /// Writes a full export CSV containing ONE ROW PER PART NUMBER.
        /// - Groups results by PartNumber
        /// - Uses the first scan result in each group as the source of dims/weight (they should be identical per part)
        /// - IMAGE_FILE_NAME will use the lowest Sequence image filename (or blank if none)
        /// - Leaves unused fields blank but includes every header
        /// </summary>
        public static void ExportOneRowPerPart(string csvPath, IEnumerable<ScanResult> results,
                                               string dimUnit = "in",
                                               string volUnit = "",
                                               string factor = "166",
                                               string siteId = "733")
        {
            if (results == null) throw new ArgumentNullException(nameof(results));

            // Ensure directory exists
            var dir = Path.GetDirectoryName(csvPath);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            var rows = results
                .Where(r => !string.IsNullOrWhiteSpace(r.PartNumber))
                .GroupBy(r => r.PartNumber.Trim(), StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();

            using var fs = new FileStream(csvPath, FileMode.Create, FileAccess.Write, FileShare.Read);
            using var w = new StreamWriter(fs, Encoding.UTF8);

            // Header
            w.WriteLine(string.Join(",", Headers));

            foreach (var g in rows)
            {
                var part = g.Key;

                // Prefer the lowest-sequence image as the representative filename (or just first)
                var representative = g
                    .OrderBy(r => r.Sequence)
                    .FirstOrDefault();

                // Build row values dictionary with blanks by default
                var vals = Headers.ToDictionary(h => h, _ => "", StringComparer.OrdinalIgnoreCase);

                vals["ITEM_ID"] = part;

                // Fill part-level measurements (use representative record; assumes part-level values are consistent)
                if (representative != null)
                {
                    // Treat 0 as "blank" (common for "not yet captured")
                    vals["NET_LENGTH"] = representative.LengthIn > 0
                        ? representative.LengthIn.ToString("F2", CultureInfo.InvariantCulture)
                        : "";

                    // NOTE: current model uses DepthIn as "Width"
                    vals["NET_WIDTH"] = representative.DepthIn > 0
                        ? representative.DepthIn.ToString("F2", CultureInfo.InvariantCulture)
                        : "";

                    vals["NET_HEIGHT"] = representative.HeightIn > 0
                        ? representative.HeightIn.ToString("F2", CultureInfo.InvariantCulture)
                        : "";

                    vals["NET_WEIGHT"] = representative.WeightLb > 0
                        ? representative.WeightLb.ToString("F2", CultureInfo.InvariantCulture)
                        : "";

                    // Optional/placeholder fields
                    vals["NET_VOLUME"] = "";
                    vals["NET_DIM_WEIGHT"] = "";

                    vals["DIM_UNIT"] = dimUnit ?? "";
                    vals["VOL_UNIT"] = volUnit ?? "";
                    vals["FACTOR"] = factor ?? "";
                    vals["SITE_ID"] = siteId ?? "";

                    // Timestamp: use "now" for the export stamp, or you can choose representative.TimeStamp
                    vals["TIME_STAMP"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

                    // Keep all OPT_INFO columns present; fill if you want later
                    vals["OPT_INFO_1"] = "";
                    vals["OPT_INFO_2"] = "";
                    vals["OPT_INFO_3"] = "";
                    vals["OPT_INFO_4"] = "";
                    vals["OPT_INFO_5"] = "";
                    vals["OPT_INFO_6"] = "";
                    vals["OPT_INFO_7"] = "";
                    vals["OPT_INFO_8"] = "";

                    vals["IMAGE_FILE_NAME"] = representative.ImageFileName ?? "";
                    vals["UPDATED"] = "Y";
                }

                // Write row in exact header order, CSV-escaped
                var row = string.Join(",", Headers.Select(h => EscapeCsv(vals[h])));
                w.WriteLine(row);
            }
        }

        private static string EscapeCsv(string? value)
        {
            value ??= "";
            bool mustQuote = value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r');
            if (value.Contains('"'))
                value = value.Replace("\"", "\"\"");
            return mustQuote ? $"\"{value}\"" : value;
        }
    }
}