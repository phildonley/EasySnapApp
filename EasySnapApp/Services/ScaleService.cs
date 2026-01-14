using System;
using System.Globalization;
using System.IO.Ports;
using System.Text;
using System.Text.RegularExpressions;

namespace EasySnapApp.Services
{
    /// <summary>
    /// One-shot scale reader for RS232 scales (via USB-to-Serial adapter).
    /// Reads ONLY when the user clicks "Capture Weight".
    ///
    /// Design goals:
    /// - Never block the UI forever
    /// - Always return a value in TEST MODE
    /// - In real mode, read a single line, parse best-effort, and throw a readable error if it can’t
    /// - Keep parsing flexible because different firmware versions output different line formats
    /// </summary>
    public class ScaleService
    {
        private readonly bool _testMode;

        // Settings (can be moved to your in-app settings pane later)
        public string PortName { get; set; } = "";     // e.g. "COM4"
        public int BaudRate { get; set; } = 9600;
        public Parity Parity { get; set; } = Parity.None;
        public int DataBits { get; set; } = 8;
        public StopBits StopBits { get; set; } = StopBits.One;

        // Some scales output CRLF, some LF, some just CR
        public string NewLine { get; set; } = "\r\n";

        // How long we wait for the scale to spit a line
        public int ReadTimeoutMs { get; set; } = 1200;

        // Last raw line for troubleshooting (you can show this in the UI status bar)
        public string LastRawLine { get; private set; } = "";

        public ScaleService(bool testMode = false)
        {
            _testMode = testMode;
        }

        /// <summary>
        /// Capture a single weight reading in POUNDS (lb).
        /// </summary>
        public double CaptureWeightLbOnce()
        {
            if (_testMode)
                return 1.23; // dummy value to keep workflow working before hardware parsing is finalized

            if (string.IsNullOrWhiteSpace(PortName))
                throw new InvalidOperationException("Scale COM port not set. Set ScaleService.PortName (e.g., COM4).");

            // Read one line
            LastRawLine = ReadOneLine();

            // Parse to pounds
            return ParseWeightToLb(LastRawLine);
        }

        private string ReadOneLine()
        {
            // Note: Do NOT keep the port open permanently yet.
            // One-shot open/read/close is more robust on locked-down machines and avoids port locking.
            using var sp = new SerialPort
            {
                PortName = PortName.Trim(),
                BaudRate = BaudRate,
                Parity = Parity,
                DataBits = DataBits,
                StopBits = StopBits,
                Handshake = Handshake.None,
                Encoding = Encoding.ASCII,
                ReadTimeout = ReadTimeoutMs,
                WriteTimeout = 500,
                NewLine = NewLine
            };

            try
            {
                sp.Open();

                // Some scales only transmit when stable; waiting for one line is fine.
                // If your model requires a poll command later, we can add it (e.g., "W\r\n").
                string line = sp.ReadLine();

                return (line ?? "").Trim();
            }
            catch (TimeoutException)
            {
                throw new TimeoutException(
                    $"Timed out reading from {PortName}. " +
                    $"Try confirming the COM port, baud rate, and that the scale is set to output via RS232.");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Scale read failed on {PortName}: {ex.Message}", ex);
            }
            finally
            {
                if (sp.IsOpen)
                    sp.Close();
            }
        }

        /// <summary>
        /// Best-effort parser. We’ll tighten this once we see actual output from your scale.
        ///
        /// Handles common patterns like:
        /// - "  1.234 lb"
        /// - "0.560kg"
        /// - "560 g"
        /// - "WT: 1.23lb"
        /// </summary>
        private double ParseWeightToLb(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                throw new FormatException("Scale output was empty.");

            // Normalize spacing
            string s = raw.Trim();

            // Extract a number + optional unit
            // Example matches: "1.23", "-0.50", "560"
            var m = Regex.Match(s, @"(-?\d+(\.\d+)?)\s*([a-zA-Z]+)?");
            if (!m.Success)
                throw new FormatException($"Could not parse weight from scale output: '{raw}'");

            double value = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            string unit = (m.Groups[3].Value ?? "").Trim().ToLowerInvariant();

            // If unit not present, assume LB (we can change later if your scale defaults to grams)
            if (string.IsNullOrEmpty(unit))
                return value;

            // Convert to pounds
            // lb
            if (unit == "lb" || unit == "lbs")
                return value;

            // kg to lb
            if (unit == "kg")
                return value * 2.2046226218;

            // g to lb
            if (unit == "g" || unit == "gram" || unit == "grams")
                return value * 0.0022046226218;

            // oz to lb
            if (unit == "oz")
                return value / 16.0;

            // Unknown unit: don’t silently guess
            throw new FormatException($"Unknown unit '{unit}' in scale output: '{raw}'");
        }
    }
}