using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EasySnapApp.Data;
using EasySnapApp.Utils;

namespace EasySnapApp.Views
{
    public partial class FileNameBuilderWindow : Window
    {
        // ── State ────────────────────────────────────────────────────────
        private readonly ObservableCollection<PatternToken> _tokens
            = new ObservableCollection<PatternToken>();

        private readonly PartsDataRepository _partsRepo;

        private string _previewPart = "0090-193";
        private string _previewTmsId = "10025142";
        private string _previewDisplayName = "SPRING";

        // Cache of all part numbers for the repair scanner (loaded once)
        private List<string> _allPartNumbers = null;

        // ── Constructor ──────────────────────────────────────────────────

        public FileNameBuilderWindow(PartsDataRepository partsRepo)
        {
            _partsRepo = partsRepo;
            InitializeComponent();

            PatternListBox.ItemsSource = _tokens;

            var saved = FilenamePattern.LoadFromSettings();
            foreach (var t in saved.Tokens)
                _tokens.Add(t);

            LoadPreviewParts();
            UpdatePreview();
        }

        // ── Preview part loading ─────────────────────────────────────────

        private void LoadPreviewParts()
        {
            PreviewPartComboBox.Items.Add("0090-193  (sample)");

            try
            {
                if (_partsRepo != null)
                {
                    var realParts = _partsRepo.GetAllPartNumbers().Take(20);
                    foreach (var pn in realParts)
                        PreviewPartComboBox.Items.Add(pn);
                }
            }
            catch { }

            PreviewPartComboBox.SelectedIndex = 0;
        }

        private void UpdatePreviewFromSelectedPart(string partNumber)
        {
            _previewPart = partNumber;
            _previewTmsId = null;   // null = missing → will be omitted
            _previewDisplayName = null;

            try
            {
                if (_partsRepo != null && !string.IsNullOrEmpty(partNumber))
                {
                    _previewTmsId = _partsRepo.GetTmsIdForPart(partNumber);
                    _previewDisplayName = _partsRepo.GetDisplayNameForPart(partNumber);
                }
            }
            catch { }
        }

        // ── Live preview ─────────────────────────────────────────────────

        private void UpdatePreview()
        {
            try
            {
                var pattern = BuildCurrentPattern();
                var preview = pattern.Evaluate(_previewPart, _previewTmsId, _previewDisplayName, 103);
                PreviewTextBlock.Text = preview + ".jpg";

                // Show hint when database fields are missing for selected part
                var missingFields = new List<string>();
                if (string.IsNullOrWhiteSpace(_previewTmsId)) missingFields.Add("TMS ID");
                if (string.IsNullOrWhiteSpace(_previewDisplayName)) missingFields.Add("Display Name");

                if (missingFields.Any() && _tokens.Any(t =>
                    t.Type == TokenType.TmsId ||
                    t.Type == TokenType.DisplayName ||
                    t.Type == TokenType.DisplayNameRaw))
                {
                    PreviewMissingTextBlock.Text =
                        $"Note: {string.Join(", ", missingFields)} not found in database for this part — " +
                        "those tokens will be omitted from the filename.";
                    PreviewMissingTextBlock.Visibility = Visibility.Visible;
                }
                else
                {
                    PreviewMissingTextBlock.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception ex)
            {
                PreviewTextBlock.Text = $"(preview error: {ex.Message})";
            }
        }

        private FilenamePattern BuildCurrentPattern()
            => new FilenamePattern { Tokens = _tokens.ToList() };

        // ── Token picker handlers ────────────────────────────────────────

        private void AddFieldToken_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button btn)) return;
            TokenType type;
            switch (btn.Tag?.ToString())
            {
                case "TmsId": type = TokenType.TmsId; break;
                case "DisplayName": type = TokenType.DisplayName; break;
                case "DisplayNameRaw": type = TokenType.DisplayNameRaw; break;
                case "PartNumber": type = TokenType.PartNumber; break;
                default: return;
            }
            AddToken(PatternToken.Field(type));
        }

        private void AddSequenceToken_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button btn)) return;
            var fmt = btn.Tag?.ToString() == "Sequence0" ? "0" : "000";
            AddToken(PatternToken.SeqToken(fmt));
        }

        private void AddSeparatorToken_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button btn)) return;
            AddToken(PatternToken.Static(btn.Tag?.ToString() ?? "."));
        }

        private void AddStaticToken_Click(object sender, RoutedEventArgs e) => CommitStaticText();

        private void StaticTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { CommitStaticText(); e.Handled = true; }
        }

        private void CommitStaticText()
        {
            var text = StaticTextBox.Text;
            if (string.IsNullOrEmpty(text)) return;
            AddToken(PatternToken.Static(text));
            StaticTextBox.Clear();
            StaticTextBox.Focus();
        }

        private void AddToken(PatternToken token)
        {
            var idx = PatternListBox.SelectedIndex;
            if (idx >= 0 && idx < _tokens.Count - 1)
                _tokens.Insert(idx + 1, token);
            else
                _tokens.Add(token);

            var newIdx = _tokens.IndexOf(token);
            PatternListBox.SelectedIndex = newIdx;
            PatternListBox.ScrollIntoView(token);
            UpdatePreview();
        }

        // ── Reorder / remove ─────────────────────────────────────────────

        private void PatternListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var idx = PatternListBox.SelectedIndex;
            MoveUpButton.IsEnabled = idx > 0;
            MoveDownButton.IsEnabled = idx >= 0 && idx < _tokens.Count - 1;
            RemoveTokenButton.IsEnabled = idx >= 0;
        }

        private void MoveUp_Click(object sender, RoutedEventArgs e)
        {
            var idx = PatternListBox.SelectedIndex;
            if (idx <= 0) return;
            var token = _tokens[idx];
            _tokens.RemoveAt(idx);
            _tokens.Insert(idx - 1, token);
            PatternListBox.SelectedIndex = idx - 1;
            UpdatePreview();
        }

        private void MoveDown_Click(object sender, RoutedEventArgs e)
        {
            var idx = PatternListBox.SelectedIndex;
            if (idx < 0 || idx >= _tokens.Count - 1) return;
            var token = _tokens[idx];
            _tokens.RemoveAt(idx);
            _tokens.Insert(idx + 1, token);
            PatternListBox.SelectedIndex = idx + 1;
            UpdatePreview();
        }

        private void RemoveToken_Click(object sender, RoutedEventArgs e)
        {
            var idx = PatternListBox.SelectedIndex;
            if (idx < 0) return;
            _tokens.RemoveAt(idx);
            if (_tokens.Count > 0)
                PatternListBox.SelectedIndex = Math.Min(idx, _tokens.Count - 1);
            UpdatePreview();
        }

        private void ClearAll_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Clear all tokens?", "Confirm",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            _tokens.Clear();
            UpdatePreview();
        }

        private void ResetDefault_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Reset to the default pattern?", "Confirm",
                    MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            _tokens.Clear();
            foreach (var t in FilenamePattern.Default.Tokens)
                _tokens.Add(t);
            UpdatePreview();
        }

        private void PreviewPart_Changed(object sender, SelectionChangedEventArgs e)
        {
            var selected = PreviewPartComboBox.SelectedItem?.ToString() ?? string.Empty;
            if (selected.Contains("(sample)"))
            {
                _previewPart = "0090-193";
                _previewTmsId = "10025142";
                _previewDisplayName = "SPRING";
            }
            else
            {
                UpdatePreviewFromSelectedPart(selected.Trim());
            }
            UpdatePreview();
        }

        // ── Dialog result ────────────────────────────────────────────────

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (_tokens.Count == 0)
            {
                MessageBox.Show("Add at least one token before saving.",
                    "Pattern Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            BuildCurrentPattern().SaveToSettings();
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        // ════════════════════════════════════════════════════════════════
        // REPAIR TAB
        // ════════════════════════════════════════════════════════════════

        private string _repairFolder = null;

        private void RepairBrowse_Click(object sender, RoutedEventArgs e)
        {
            using (var dlg = new System.Windows.Forms.FolderBrowserDialog())
            {
                dlg.Description = "Select the folder containing images to repair";
                dlg.ShowNewFolderButton = false;

                if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    _repairFolder = dlg.SelectedPath;
                    RepairFolderTextBox.Text = _repairFolder;
                    RepairRunButton.IsEnabled = true;
                    RepairLog($"Folder selected: {_repairFolder}");
                }
            }
        }

        private void RepairRun_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_repairFolder) || !Directory.Exists(_repairFolder))
            {
                MessageBox.Show("Please select a valid folder first.",
                    "No Folder", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_partsRepo == null)
            {
                MessageBox.Show("Database is not available.",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            bool isDryRun = RepairDryRunCheckBox.IsChecked == true;
            bool subFolders = RepairSubfoldersCheckBox.IsChecked == true;

            RepairLogTextBox.Clear();
            RepairSummaryTextBlock.Text = "Scanning...";
            RepairLog(isDryRun
                ? "DRY RUN — no files will be changed."
                : "LIVE RUN — files will be renamed.");
            RepairLog($"Folder: {_repairFolder}");
            RepairLog(string.Empty);

            // Load all known part numbers once for exact matching
            if (_allPartNumbers == null)
            {
                try { _allPartNumbers = _partsRepo.GetAllPartNumbers(); }
                catch { _allPartNumbers = new List<string>(); }
            }

            var pattern = FilenamePattern.LoadFromSettings();
            var searchOption = subFolders
                ? SearchOption.AllDirectories
                : SearchOption.TopDirectoryOnly;

            var files = Directory.GetFiles(_repairFolder, "*.jpg", searchOption)
                                 .Where(f => !Path.GetFileName(f).Contains(".thumb."))
                                 .OrderBy(f => f)
                                 .ToList();

            RepairLog($"Found {files.Count} image file(s) to evaluate.");
            RepairLog(string.Empty);

            int scanned = 0, renamed = 0, skipped = 0, failed = 0, noData = 0;

            foreach (var filePath in files)
            {
                scanned++;
                var fileName = Path.GetFileNameWithoutExtension(filePath);
                var ext = Path.GetExtension(filePath).ToLowerInvariant();
                var dir = Path.GetDirectoryName(filePath);

                // ── Extract part number and sequence from existing filename ──
                if (!TryParseFilename(fileName, out string partNumber, out int sequence))
                {
                    RepairLog($"  SKIP  {fileName}{ext}  (could not parse part number / sequence)");
                    skipped++;
                    continue;
                }

                // ── Database lookup — exact match only ──────────────────────
                string tmsId = null;
                string displayName = null;
                try
                {
                    tmsId = _partsRepo.GetTmsIdForPart(partNumber);
                    displayName = _partsRepo.GetDisplayNameForPart(partNumber);
                }
                catch (Exception ex)
                {
                    RepairLog($"  ERROR {fileName}{ext}  (DB lookup failed: {ex.Message})");
                    failed++;
                    continue;
                }

                bool hasDatabaseData = !string.IsNullOrWhiteSpace(tmsId)
                                    || !string.IsNullOrWhiteSpace(displayName);

                if (!hasDatabaseData)
                {
                    RepairLog($"  NODATA  {fileName}{ext}  (part '{partNumber}' not in database)");
                    noData++;
                    continue;
                }

                // ── Build what the name SHOULD be ───────────────────────────
                var targetStem = pattern.Evaluate(partNumber, tmsId, displayName, sequence);
                var targetFile = targetStem + ext;
                var targetPath = Path.Combine(dir, targetFile);

                // Already correct?
                if (string.Equals(Path.GetFileName(filePath), targetFile,
                                   StringComparison.OrdinalIgnoreCase))
                {
                    RepairLog($"  OK    {fileName}{ext}");
                    skipped++;
                    continue;
                }

                // ── Rename ───────────────────────────────────────────────────
                RepairLog($"  RENAME  {fileName}{ext}");
                RepairLog($"       => {targetFile}");

                if (!isDryRun)
                {
                    try
                    {
                        // Rename main image
                        if (File.Exists(targetPath)) File.Delete(targetPath);
                        File.Move(filePath, targetPath);

                        // Rename thumbnail if it exists
                        var thumbStem = Path.GetFileNameWithoutExtension(filePath) + ".thumb";
                        var thumbPath = Path.Combine(dir, thumbStem + ext);
                        if (File.Exists(thumbPath))
                        {
                            var newThumbPath = Path.Combine(dir,
                                FileNameGenerator.BuildThumbnailStem(targetStem) + ext);
                            if (File.Exists(newThumbPath)) File.Delete(newThumbPath);
                            File.Move(thumbPath, newThumbPath);
                        }

                        renamed++;
                    }
                    catch (Exception ex)
                    {
                        RepairLog($"       FAILED: {ex.Message}");
                        failed++;
                    }
                }
                else
                {
                    renamed++; // count as "would rename" in dry run
                }
            }

            RepairLog(string.Empty);
            RepairLog($"Done. Scanned: {scanned}  " +
                      $"{(isDryRun ? "Would rename" : "Renamed")}: {renamed}  " +
                      $"Already correct / skipped: {skipped}  " +
                      $"No database data: {noData}  " +
                      $"Errors: {failed}");

            RepairSummaryTextBlock.Text =
                isDryRun
                ? $"Dry run complete — {renamed} file(s) would be renamed, {noData} still missing data."
                : $"Complete — {renamed} renamed, {noData} still missing data, {failed} errors.";

            RepairLogScrollViewer.ScrollToEnd();
        }

        /// <summary>
        /// Parse a filename stem into (partNumber, sequence).
        /// 
        /// Strategy: the sequence is always the last dot-segment that is all digits.
        /// Everything to the left of it is the "prefix". We then scan the prefix for
        /// a known part number using EXACT match against dot-separated segments.
        /// This prevents partial matches (e.g. "3344GT" matching inside "1234-3344GT").
        /// </summary>
        private bool TryParseFilename(string stem, out string partNumber, out int sequence)
        {
            partNumber = null;
            sequence = 0;

            if (string.IsNullOrEmpty(stem)) return false;

            var segments = stem.Split('.');

            // Find rightmost all-digit segment — that is the sequence
            int seqIdx = -1;
            for (int i = segments.Length - 1; i >= 0; i--)
            {
                if (segments[i].Length > 0 && segments[i].All(char.IsDigit))
                {
                    seqIdx = i;
                    break;
                }
            }

            if (seqIdx < 0) return false;

            if (!int.TryParse(segments[seqIdx], out sequence)) return false;

            // Everything before the sequence index is the prefix
            var prefixSegments = segments.Take(seqIdx).ToArray();
            if (prefixSegments.Length == 0) return false;

            // Try to find an exact part-number match in the prefix.
            // Check all possible consecutive-segment combinations to handle part numbers
            // that may contain dots themselves (though our system uses dots as separators,
            // some edge-case imports might have them).
            if (_allPartNumbers != null && _allPartNumbers.Count > 0)
            {
                // Longest-match first: try joining more segments before fewer
                for (int len = prefixSegments.Length; len >= 1; len--)
                {
                    for (int start = 0; start <= prefixSegments.Length - len; start++)
                    {
                        var candidate = string.Join(".", prefixSegments.Skip(start).Take(len));
                        if (_allPartNumbers.Contains(candidate, StringComparer.OrdinalIgnoreCase))
                        {
                            partNumber = candidate;
                            return true;
                        }
                    }
                }
            }

            // Fallback when we have no part list or no match: treat the LAST prefix segment
            // as the sequence context and the rest as the part number.
            // e.g. "0090-193.103" → partNumber = "0090-193"
            partNumber = string.Join(".", prefixSegments);
            return !string.IsNullOrEmpty(partNumber);
        }

        private void RepairLog(string message)
        {
            RepairLogTextBox.AppendText(message + Environment.NewLine);
        }

        private void RepairCopyLog_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(RepairLogTextBox.Text);
                RepairSummaryTextBlock.Text = "Log copied to clipboard.";
            }
            catch { }
        }
    }
}