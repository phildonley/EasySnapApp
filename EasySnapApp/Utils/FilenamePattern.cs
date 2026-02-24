using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace EasySnapApp.Utils
{
    /// <summary>
    /// Phase 3: An ordered list of PatternTokens that evaluates into a filename.
    /// Missing field tokens are silently dropped along with their adjacent separators.
    /// Serializes to a pipe-delimited string for app settings storage.
    /// </summary>
    public class FilenamePattern
    {
        private const char TokenSeparator = '|';

        public List<PatternToken> Tokens { get; set; } = new List<PatternToken>();

        // ── Built-in patterns ────────────────────────────────────────────

        public static FilenamePattern Default => new FilenamePattern
        {
            Tokens = new List<PatternToken>
            {
                PatternToken.Field(TokenType.TmsId),
                PatternToken.Static("."),
                PatternToken.Field(TokenType.DisplayName),
                PatternToken.Static("."),
                PatternToken.Field(TokenType.PartNumber),
                PatternToken.Static("."),
                PatternToken.SeqToken("settings") // format derived from settings at eval time
            }
        };

        public static FilenamePattern Legacy => new FilenamePattern
        {
            Tokens = new List<PatternToken>
            {
                PatternToken.Field(TokenType.PartNumber),
                PatternToken.Static("."),
                PatternToken.SeqToken("settings") // format derived from settings at eval time
            }
        };

        // ── Evaluation ───────────────────────────────────────────────────

        /// <summary>
        /// Build the filename string from the pattern plus runtime values.
        /// 
        /// Missing field behaviour: if a field token resolves to empty, it is dropped
        /// along with the nearest adjacent separator so no orphaned dots/underscores remain.
        /// 
        ///   [TmsId][.][DisplayName][.][PartNumber][.][Seq]
        ///   TmsId missing  →  [DisplayName][.][PartNumber][.][Seq]
        ///   Both missing   →  [PartNumber][.][Seq]
        /// 
        /// Extension (.jpg) is NOT included — append at the call site.
        /// </summary>
        public string Evaluate(
            string partNumber,
            string tmsId,
            string displayName,
            int sequence)
        {
            if (Tokens == null || Tokens.Count == 0)
                return Legacy.Evaluate(partNumber, tmsId, displayName, sequence);

            // Step 1: Resolve every token to a string (empty = missing/skip)
            var resolved = ResolveTokens(partNumber, tmsId, displayName, sequence);

            // Step 2: Collapse empty field tokens and their orphaned separators
            var collapsed = CollapseEmpty(resolved);

            // Step 3: Join and clean up any leftover separator runs
            var result = string.Concat(collapsed).Trim();

            // Final safety net: always return a usable name
            if (string.IsNullOrWhiteSpace(result))
            {
                try
                {
                    var pad = Properties.Settings.Default.SequencePadding;
                    var digits = Properties.Settings.Default.SequenceDigits;
                    var fmt = (pad && digits > 0) ? new string('0', digits) : "0";
                    result = $"{partNumber ?? "capture"}.{sequence.ToString(fmt)}";
                }
                catch { result = $"{partNumber ?? "capture"}.{sequence}"; }
            }

            return result;
        }

        // ── Step 1: token resolution ──────────────────────────────────────

        private struct ResolvedToken
        {
            public bool IsField;   // true = came from a field token (may be empty)
            public bool IsSep;     // true = static separator candidate
            public string Value;
        }

        private List<ResolvedToken> ResolveTokens(
            string partNumber, string tmsId, string displayName, int sequence)
        {
            var list = new List<ResolvedToken>();

            foreach (var token in Tokens)
            {
                switch (token.Type)
                {
                    case TokenType.StaticText:
                        list.Add(new ResolvedToken
                        {
                            IsField = false,
                            IsSep = true,   // treat all statics as potential separators
                            Value = token.Value ?? string.Empty
                        });
                        break;

                    case TokenType.PartNumber:
                        list.Add(new ResolvedToken
                        {
                            IsField = true,
                            Value = string.IsNullOrWhiteSpace(partNumber)
                                          ? string.Empty
                                          : partNumber.Trim()
                        });
                        break;

                    case TokenType.TmsId:
                        list.Add(new ResolvedToken
                        {
                            IsField = true,
                            Value = string.IsNullOrWhiteSpace(tmsId)
                                          ? string.Empty
                                          : tmsId.Trim()
                        });
                        break;

                    case TokenType.DisplayName:
                        var sanName = FileNameGenerator.SanitizeDisplayName(displayName);
                        list.Add(new ResolvedToken
                        {
                            IsField = true,
                            Value = sanName == "unknown" && string.IsNullOrWhiteSpace(displayName)
                                          ? string.Empty  // genuinely missing
                                          : sanName
                        });
                        break;

                    case TokenType.DisplayNameRaw:
                        list.Add(new ResolvedToken
                        {
                            IsField = true,
                            Value = string.IsNullOrWhiteSpace(displayName)
                                          ? string.Empty
                                          : displayName.Trim()
                        });
                        break;

                    case TokenType.Sequence:
                        // Always derive format from user settings — ignore the
                        // token's stored format string so digit/padding changes
                        // take effect immediately without re-saving the pattern.
                        string seqStr;
                        try
                        {
                            var pad = Properties.Settings.Default.SequencePadding;
                            var digits = Properties.Settings.Default.SequenceDigits;
                            var fmt = (pad && digits > 0)
                                             ? new string('0', digits)
                                             : "0";
                            seqStr = sequence.ToString(fmt);
                        }
                        catch { seqStr = sequence.ToString(); }
                        list.Add(new ResolvedToken { IsField = true, Value = seqStr });
                        break;
                }
            }

            return list;
        }

        // ── Step 2: collapse empty fields + orphaned separators ───────────

        /// <summary>
        /// Walk the resolved list and remove:
        ///   - any field token with empty value
        ///   - the separator immediately AFTER an empty field (preferred)
        ///   - OR the separator immediately BEFORE if no separator follows
        /// Then strip any leading/trailing separators that remain.
        /// </summary>
        private List<string> CollapseEmpty(List<ResolvedToken> tokens)
        {
            // Mark which indices should be dropped
            var drop = new bool[tokens.Count];

            for (int i = 0; i < tokens.Count; i++)
            {
                if (!tokens[i].IsField || !string.IsNullOrEmpty(tokens[i].Value))
                    continue;

                // This field is empty — drop it
                drop[i] = true;

                // Prefer dropping the separator AFTER it
                if (i + 1 < tokens.Count && tokens[i + 1].IsSep)
                {
                    drop[i + 1] = true;
                }
                // Otherwise drop the separator BEFORE it
                else if (i - 1 >= 0 && tokens[i - 1].IsSep)
                {
                    drop[i - 1] = true;
                }
            }

            var result = new List<string>();
            for (int i = 0; i < tokens.Count; i++)
            {
                if (!drop[i])
                    result.Add(tokens[i].Value);
            }

            // Strip any leading or trailing separator-only entries
            while (result.Count > 0 && IsPureSeparator(result[0]))
                result.RemoveAt(0);

            while (result.Count > 0 && IsPureSeparator(result[result.Count - 1]))
                result.RemoveAt(result.Count - 1);

            return result;
        }

        private static readonly char[] _sepChars = { '.', '_', '-', ' ' };

        private bool IsPureSeparator(string s)
        {
            if (string.IsNullOrEmpty(s)) return true;
            foreach (char c in s)
                if (!_sepChars.Contains(c)) return false;
            return true;
        }

        // ── Preview helper ────────────────────────────────────────────────

        public string PreviewWithSampleData(
            string partNumber = "0090-193",
            string tmsId = "10025142",
            string displayName = "SPRING",
            int sequence = 103)
        {
            return Evaluate(partNumber, tmsId, displayName, sequence);
        }

        // ── Serialization ─────────────────────────────────────────────────

        public string Serialize()
        {
            if (Tokens == null || Tokens.Count == 0) return string.Empty;
            return string.Join(TokenSeparator.ToString(), Tokens.Select(t => t.Serialize()));
        }

        public static FilenamePattern Deserialize(string s)
        {
            var pattern = new FilenamePattern();
            if (string.IsNullOrWhiteSpace(s))
            {
                pattern.Tokens = Default.Tokens;
                return pattern;
            }

            var parts = s.Split(TokenSeparator);
            pattern.Tokens = parts
                .Select(p => PatternToken.Deserialize(p))
                .Where(t => t != null)
                .ToList();

            if (pattern.Tokens.Count == 0)
                pattern.Tokens = Default.Tokens;

            return pattern;
        }

        public static FilenamePattern LoadFromSettings()
        {
            try
            {
                var stored = Properties.Settings.Default.FilenamePatternString;
                if (!string.IsNullOrWhiteSpace(stored))
                    return Deserialize(stored);
            }
            catch { }
            return Default;
        }

        public void SaveToSettings()
        {
            try
            {
                Properties.Settings.Default.FilenamePatternString = Serialize();
                Properties.Settings.Default.Save();
            }
            catch { }
        }
    }
}