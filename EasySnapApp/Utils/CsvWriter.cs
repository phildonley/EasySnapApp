using EasySnapApp.Models;
using EasySnapApp.Views;
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
        // EXACT header specification - 25 columns in exact order
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
            "NET_DIM_WGT",
            "DIM_UNIT",
            "WGT_UNIT",
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
        }; // <-- FIX #1: missing semicolon

        /// <summary>
        /// Parse capture timestamp from ScanResult.TimeStamp field
        /// Expected formats: "yyyyMMdd_HHmmss" or similar
        /// </summary>
        private static DateTime? ParseCaptureTimestamp(string? timeStamp)
        {
            if (string.IsNullOrWhiteSpace(timeStamp)) return null;

            // Try common formats
            var formats = new[]
            {
                "yyyyMMdd_HHmmss",
                "yyyyMMdd_HHmm",
                "yyyy-MM-dd HH:mm:ss",
                "MM/dd/yyyy"
            };

            foreach (var format in formats)
            {
                if (DateTime.TryParseExact(timeStamp, format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var result))
                {
                    return result;
                }
            }

            return null;
        } // <-- FIX #2: removed stray semicolon after method block

        /// <summary>
        /// Exports CSV with exact specification requirements and validation logging
        /// </summary>
        public static void ExportOneRowPerPart(string csvPath, IEnumerable<ImageRecordViewModel> imageRecords, Action<string> logMessage = null)
        {
            if (imageRecords == null) throw new ArgumentNullException(nameof(imageRecords));

            // Load export settings
            var settings = Properties.Settings.Default;
            var dimUnit = settings.ExportDimUnit ?? "in";
            var wgtUnit = settings.ExportWgtUnit ?? "lb";
            var volUnit = settings.ExportVolUnit ?? "in";
            var factor = double.Parse(settings.ExportFactor ?? "166", CultureInfo.InvariantCulture);
            var siteId = settings.ExportSiteId ?? "733";
            var optInfo2 = settings.ExportOptInfo2 ?? "Y";
            var optInfo3 = settings.ExportOptInfo3 ?? "Y";

            // Log factor warning if using metric units with default factor (once per export)
            if ((dimUnit == "cm" || wgtUnit == "kg") && Math.Abs(factor - 166) < 0.01)
            {
                logMessage?.Invoke($"Warning: Using metric units ({dimUnit}/{wgtUnit}) with factor {factor} - verify factor is appropriate for your system");
            }

            // Ensure directory exists
            var dir = Path.GetDirectoryName(csvPath);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            var rows = imageRecords
                .Where(r => !string.IsNullOrWhiteSpace(r.PartNumber))
                .GroupBy(r => r.PartNumber.Trim(), StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();

            // Use Windows CRLF line endings
            using var fs = new FileStream(csvPath, FileMode.Create, FileAccess.Write, FileShare.Read);
            using var w = new StreamWriter(fs, new UTF8Encoding(false)) { NewLine = "\r\n" };

            // Header line
            w.WriteLine(string.Join(",", Headers));

            int exportedParts = 0;
            int errorCount = 0;

            foreach (var g in rows)
            {
                var part = g.Key;
                var representative = g.OrderBy(r => r.Sequence).FirstOrDefault();

                // Initialize all fields to blank
                var vals = Headers.ToDictionary(h => h, _ => "", StringComparer.OrdinalIgnoreCase);

                // Required fields
                vals["ITEM_ID"] = part.Replace(",", ""); // Sanitize commas from part number
                vals["ITEM_TYPE"] = ""; // blank per spec
                vals["DESCRIPTION"] = ""; // blank per spec

                if (representative != null)
                {
                    // DEBUG: Log the raw values to understand what units we're actually getting
                    logMessage?.Invoke($"DEBUG: Raw ViewModel values for {part} - Length: {representative.LengthIn}, Width: {representative.DepthIn}, Height: {representative.HeightIn}, Weight: {representative.WeightLb}");

                    // ImageRecordViewModel stores data in inches/pounds (from UI)
                    // But we need to convert according to DIMS export settings
                    var sourceLengthIn = representative.LengthIn ?? 0.0;
                    var sourceWidthIn = representative.DepthIn ?? 0.0; // DepthIn = Width in ViewModel
                    var sourceHeightIn = representative.HeightIn ?? 0.0;
                    var sourceWeightLb = representative.WeightLb ?? 0.0;

                    // Convert from source units (inches/lb) to export units per DIMS settings
                    var lengthConverted = ConvertLength(sourceLengthIn, "in", dimUnit);
                    var widthConverted = ConvertLength(sourceWidthIn, "in", dimUnit);
                    var heightConverted = ConvertLength(sourceHeightIn, "in", dimUnit);
                    var weightConverted = ConvertWeight(sourceWeightLb, "lb", wgtUnit);

                    // DEBUG: Log the converted values
                    logMessage?.Invoke($"DEBUG: Converted values for {part} - Length: {lengthConverted}{dimUnit}, Width: {widthConverted}{dimUnit}, Height: {heightConverted}{dimUnit}, Weight: {weightConverted}{wgtUnit}");

                    // Format to 0-4 decimal places, blank if invalid
                    vals["NET_LENGTH"] = FormatNumeric(lengthConverted);
                    vals["NET_WIDTH"] = FormatNumeric(widthConverted);
                    vals["NET_HEIGHT"] = FormatNumeric(heightConverted);
                    vals["NET_WEIGHT"] = FormatNumeric(weightConverted);

                    // Compute volume from converted dimensions
                    var volume = (lengthConverted > 0 && widthConverted > 0 && heightConverted > 0)
                        ? lengthConverted * widthConverted * heightConverted
                        : 0.0;
                    vals["NET_VOLUME"] = FormatNumeric(volume);

                    // Compute dimensional weight from volume and factor
                    var dimWeight = (volume > 0 && factor > 0) ? volume / factor : 0.0;
                    vals["NET_DIM_WGT"] = FormatNumeric(dimWeight);
                }

                // Unit labels
                vals["DIM_UNIT"] = dimUnit;
                vals["WGT_UNIT"] = wgtUnit;
                vals["VOL_UNIT"] = volUnit;

                // Configuration values
                vals["FACTOR"] = factor.ToString("0", CultureInfo.InvariantCulture);
                vals["SITE_ID"] = siteId;

                // Parse capture timestamp from representative image, fallback to now
                var captureDate = ParseCaptureTimestamp(representative?.TimeStamp) ?? DateTime.Now;
                vals["TIME_STAMP"] = captureDate.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture);

                // OPT_INFO fields per spec
                vals["OPT_INFO_1"] = ""; // blank
                vals["OPT_INFO_2"] = optInfo2; // configurable, default Y
                vals["OPT_INFO_3"] = optInfo3; // configurable, default Y
                vals["OPT_INFO_4"] = ""; // blank
                vals["OPT_INFO_5"] = ""; // blank
                vals["OPT_INFO_6"] = ""; // blank
                vals["OPT_INFO_7"] = ""; // blank
                vals["OPT_INFO_8"] = "0"; // default 0 per spec

                // Final fields
                vals["IMAGE_FILE_NAME"] = ""; // blank per spec
                vals["UPDATED"] = "N"; // always N per spec

                // Write row in exact header order with validation
                var rowValues = Headers.Select(h => EscapeCsv(vals[h])).ToList();
                var row = string.Join(",", rowValues);

                // Validation: count commas to ensure 25 columns (24 commas = 25 fields)
                var commaCount = row.Count(c => c == ',');
                if (commaCount != 24)
                {
                    var errorMsg = $"Column alignment error for part {part}: expected 24 commas (25 fields), got {commaCount} commas";
                    logMessage?.Invoke(errorMsg);
                    errorCount++;
                    continue; // Skip this part and continue with others
                }

                w.WriteLine(row);
                exportedParts++;
            }

            // Log export summary
            logMessage?.Invoke($"Export finished; parts exported: {exportedParts}; errors: {errorCount}");
        }

        /// <summary>
        /// Convert length between units (inches <-> cm)
        /// </summary>
        private static double ConvertLength(double value, string fromUnit, string toUnit)
        {
            if (value <= 0) return 0;

            fromUnit = fromUnit.ToLowerInvariant();
            toUnit = toUnit.ToLowerInvariant();

            if (fromUnit == toUnit) return value;

            // Convert to inches first
            double inches = fromUnit == "cm" ? value / 2.54 : value;

            // Convert from inches to target
            return toUnit == "cm" ? inches * 2.54 : inches;
        }

        /// <summary>
        /// Convert weight between units (pounds <-> kg)
        /// </summary>
        private static double ConvertWeight(double value, string fromUnit, string toUnit)
        {
            if (value <= 0) return 0;

            fromUnit = fromUnit.ToLowerInvariant();
            toUnit = toUnit.ToLowerInvariant();

            if (fromUnit == toUnit) return value;

            // Convert to pounds first
            double pounds = fromUnit == "kg" ? value / 0.45359237 : value;

            // Convert from pounds to target
            return toUnit == "kg" ? pounds * 0.45359237 : pounds;
        }

        /// <summary>
        /// Format numeric value for CSV export: 0-4 decimals, round to 4 max, trim trailing zeros
        /// Examples: 0 → "0", 2 → "2", 2.5 → "2.5", 2.2506 → "2.2506", 2.25064 → "2.2506", 2.25065 → "2.2507"
        /// </summary>
        private static string FormatNumeric(double value)
        {
            if (value < 0 || double.IsNaN(value) || double.IsInfinity(value)) return ""; // Blank for invalid values

            // Round to 4 decimal places maximum
            double rounded = Math.Round(value, 4, MidpointRounding.AwayFromZero);

            // Format with up to 4 decimals, then trim trailing zeros and decimal point
            string formatted = rounded.ToString("0.####", CultureInfo.InvariantCulture);

            return formatted;
        }

        /// <summary>
        /// CSV escape - only quote if necessary
        /// </summary>
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