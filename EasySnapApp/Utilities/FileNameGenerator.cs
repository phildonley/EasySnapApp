using System;
using System.IO;
using System.Text;

namespace EasySnapApp.Utils
{
    /// <summary>
    /// Phase 3: Dynamic filename generation using a user-configurable pattern.
    /// Pattern is stored in app settings and loaded at capture time.
    /// Falls back to legacy format when no pattern is saved.
    /// </summary>
    public static class FileNameGenerator
    {
        private static readonly char[] _invalidChars = Path.GetInvalidFileNameChars();

        // ── Sanitizer (public so FilenamePattern.Evaluate can call it) ───

        /// <summary>
        /// Sanitize a display name for use as a filename segment.
        /// Rules: lowercase, spaces/hyphens → underscore, dots and invalid chars stripped,
        ///        consecutive underscores collapsed, leading/trailing underscores trimmed,
        ///        max 40 characters.
        /// Examples: "DOOR FRAME" → "door_frame"   "SPRING" → "spring"   "CAM/SHAFT" → "camshaft"
        /// </summary>
        public static string SanitizeDisplayName(string displayName)
        {
            if (string.IsNullOrWhiteSpace(displayName))
                return "unknown";

            var input = displayName.Trim().ToLowerInvariant();
            var sb = new StringBuilder(input.Length);

            foreach (char c in input)
            {
                if (c == ' ' || c == '-')
                    sb.Append('_');
                else if (c == '.' || Array.IndexOf(_invalidChars, c) >= 0)
                    continue;   // drop dots (our separator) and invalid chars
                else
                    sb.Append(c);
            }

            var result = sb.ToString()
                           .Replace("__", "_")
                           .Trim('_');

            if (string.IsNullOrEmpty(result))
                return "unknown";

            return result.Length > 40 ? result.Substring(0, 40).TrimEnd('_') : result;
        }

        // ── Sequence formatter ────────────────────────────────────────────

        /// <summary>
        /// Format a sequence number using the user's current SequenceDigits
        /// and SequencePadding settings. Used by all code paths so the
        /// physical filename always matches the UI display.
        /// </summary>
        private static string FormatSequence(int sequence)
        {
            try
            {
                var pad = Properties.Settings.Default.SequencePadding;
                var digits = Properties.Settings.Default.SequenceDigits;
                if (pad && digits > 0)
                    return sequence.ToString(new string('0', digits));
                return sequence.ToString();
            }
            catch
            {
                return sequence.ToString("000"); // safe default
            }
        }

        // ── Primary public API ────────────────────────────────────────────

        /// <summary>
        /// Phase 3: Generate a filename using the active pattern from settings.
        /// When tmsId/displayName are unavailable the pattern evaluates gracefully
        /// (field tokens output placeholder text so the file is still saved).
        /// Extension (e.g. ".jpg") is NOT included — append at the call site.
        /// </summary>
        public static string GenerateFileName(
            string partNumber,
            int sequence,
            string tmsId = null,
            string displayName = null)
        {
            var pattern = FilenamePattern.LoadFromSettings();
            return pattern.Evaluate(partNumber, tmsId, displayName, sequence);
        }

        /// <summary>
        /// Phase 3: Build the thumbnail name from any base filename.
        /// Example: "10025142.spring.0090-193.103" → "10025142.spring.0090-193.103.thumb"
        /// Extension is added by the caller.
        /// </summary>
        public static string BuildThumbnailStem(string baseStem)
        {
            return string.IsNullOrEmpty(baseStem) ? "thumb" : baseStem + ".thumb";
        }

        // ── Backward-compatible file rename ──────────────────────────────

        /// <summary>
        /// Phase 3: Rename a captured image (and its thumbnail) using the active pattern.
        /// Accepts optional tmsId/displayName — falls back gracefully if not supplied.
        /// Returns (newFullPath, newThumbPath). newThumbPath is null if no thumbnail existed.
        /// </summary>
        public static (string newFullPath, string newThumbPath) RenameCapture(
            string currentFullPath,
            string currentThumbPath,
            string tmsId,
            string displayName,
            string partNumber,
            int sequence)
        {
            var dir = Path.GetDirectoryName(currentFullPath) ?? string.Empty;
            var ext = Path.GetExtension(currentFullPath).ToLowerInvariant();   // normalise to lowercase

            // Build new base name from pattern
            var newStem = GenerateFileName(partNumber, sequence, tmsId, displayName);
            var newFullPath = Path.Combine(dir, newStem + ext);

            // Rename main image
            if (!string.Equals(currentFullPath, newFullPath, StringComparison.OrdinalIgnoreCase))
            {
                if (File.Exists(newFullPath)) File.Delete(newFullPath);
                File.Move(currentFullPath, newFullPath);
            }

            // Rename thumbnail
            string newThumbPath = null;
            if (!string.IsNullOrEmpty(currentThumbPath) && File.Exists(currentThumbPath))
            {
                var thumbExt = Path.GetExtension(currentThumbPath).ToLowerInvariant();
                var newThumbStem = BuildThumbnailStem(newStem);
                newThumbPath = Path.Combine(dir, newThumbStem + thumbExt);

                if (!string.Equals(currentThumbPath, newThumbPath, StringComparison.OrdinalIgnoreCase))
                {
                    if (File.Exists(newThumbPath)) File.Delete(newThumbPath);
                    File.Move(currentThumbPath, newThumbPath);
                }
            }

            return (newFullPath, newThumbPath);
        }

        // ── Legacy explicit methods (kept for any callers that use them) ─

        /// <summary>Legacy: explicit dynamic name without pattern system</summary>
        public static string GenerateDynamicFileName(
            string tmsId, string displayName, string partNumber, int sequence, string extension = ".jpg")
        {
            var sanitized = SanitizeDisplayName(displayName);
            return $"{tmsId}.{sanitized}.{partNumber}.{FormatSequence(sequence)}{extension}";
        }

        /// <summary>Legacy: fallback name when no parts data available</summary>
        public static string GenerateFallbackFileName(
            string partNumber, int sequence, string extension = ".jpg")
        {
            return $"{partNumber}.{FormatSequence(sequence)}{extension}";
        }

        /// <summary>Legacy: thumbnail path from full image path</summary>
        public static string BuildThumbnailFileName(string baseFileName)
        {
            if (string.IsNullOrEmpty(baseFileName)) return null;
            var ext = Path.GetExtension(baseFileName);
            var stem = Path.GetFileNameWithoutExtension(baseFileName);
            return $"{stem}.thumb{ext}";
        }
    }
}