using System;

namespace EasySnapApp.Utils
{
    /// <summary>
    /// Phase 3: Token types available in a filename pattern
    /// </summary>
    public enum TokenType
    {
        StaticText,     // Literal user-entered text or separator
        PartNumber,     // The scanned Connection Key (e.g. 0090-193)
        TmsId,          // TMS ID from parts database (e.g. 10025142)
        DisplayName,    // Sanitized display name from parts database (e.g. spring)
        DisplayNameRaw, // Raw display name, no sanitization (e.g. SPRING)
        Sequence        // Capture sequence number with configurable format
    }

    /// <summary>
    /// Phase 3: One element of a configurable filename pattern.
    /// Serializes to "TYPE:value" for storage in app settings.
    /// </summary>
    public class PatternToken
    {
        public TokenType Type { get; set; }

        /// <summary>
        /// For StaticText: the literal string (e.g. ".", "_", "Image")
        /// For Sequence:   the numeric format string (e.g. "000", "0")
        /// For all others: ignored
        /// </summary>
        public string Value { get; set; }

        // ── Factory helpers ──────────────────────────────────────────────

        public static PatternToken Static(string text)
            => new PatternToken { Type = TokenType.StaticText, Value = text };

        public static PatternToken Field(TokenType type)
            => new PatternToken { Type = type };

        public static PatternToken SeqToken(string format = "000")
            => new PatternToken { Type = TokenType.Sequence, Value = format };

        // ── Display ──────────────────────────────────────────────────────

        /// <summary>Human-readable label shown in the pattern builder list</summary>
        public string DisplayLabel
        {
            get
            {
                switch (Type)
                {
                    case TokenType.StaticText:
                        // Show spaces as visible placeholder
                        return $"\"{(string.IsNullOrEmpty(Value) ? " " : Value)}\"";
                    case TokenType.PartNumber: return "[Part Number]";
                    case TokenType.TmsId: return "[TMS ID]";
                    case TokenType.DisplayName: return "[Display Name]";
                    case TokenType.DisplayNameRaw: return "[Display Name (Raw)]";
                    case TokenType.Sequence: return $"[Sequence :{Value ?? "000"}]";
                    default: return $"[{Type}]";
                }
            }
        }

        // ── Serialization ────────────────────────────────────────────────

        private const char Delimiter = ':';

        /// <summary>Serialize to a single-field string for storage</summary>
        public string Serialize()
        {
            // Escape any colons in the value so we can split reliably
            var safeValue = (Value ?? string.Empty).Replace("\\", "\\\\").Replace(":", "\\:");
            return $"{(int)Type}{Delimiter}{safeValue}";
        }

        /// <summary>Deserialize from the format produced by Serialize()</summary>
        public static PatternToken Deserialize(string s)
        {
            if (string.IsNullOrEmpty(s))
                return Static(".");

            var idx = s.IndexOf(Delimiter);
            if (idx < 0)
                return Static(s);

            var typePart = s.Substring(0, idx);
            var valuePart = s.Substring(idx + 1)
                             .Replace("\\:", ":")
                             .Replace("\\\\", "\\");

            if (!int.TryParse(typePart, out int typeInt))
                return Static(s);

            return new PatternToken
            {
                Type = (TokenType)typeInt,
                Value = valuePart
            };
        }

        public override string ToString() => DisplayLabel;
    }
}