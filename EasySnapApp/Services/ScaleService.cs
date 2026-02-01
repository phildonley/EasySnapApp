using System;
using System.Globalization;
using System.IO.Ports;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace EasySnapApp.Services
{
    /// <summary>
    /// Enhanced scale service with VEVOR support, polling commands, and tare functionality.
    /// Supports both continuous output scales and command-response scales.
    /// </summary>
    public class ScaleService
    {
        private readonly bool _testMode;

        // Settings
        public string PortName { get; set; } = "";
        public int BaudRate { get; set; } = 9600;
        public Parity Parity { get; set; } = Parity.None;
        public int DataBits { get; set; } = 8;
        public StopBits StopBits { get; set; } = StopBits.One;
        public string NewLine { get; set; } = "\r\n";
        public int ReadTimeoutMs { get; set; } = 2000; // Increased for VEVOR
        public int WriteTimeoutMs { get; set; } = 1000;

        // Last raw line for troubleshooting
        public string LastRawLine { get; private set; } = "";

        // Scale communication modes
        public enum ScaleMode
        {
            Continuous,  // Scale continuously outputs data
            Poll,        // Need to send command to get reading
            Auto         // Try to detect automatically
        }

        public ScaleMode Mode { get; set; } = ScaleMode.Auto;

        public ScaleService(bool testMode = false)
        {
            _testMode = testMode;
        }

        /// <summary>
        /// Capture a single weight reading in POUNDS (lb)
        /// </summary>
        public double CaptureWeightLbOnce()
        {
            if (_testMode)
                return 1.23 + (new Random().NextDouble() * 0.5); // Vary the dummy value

            if (string.IsNullOrWhiteSpace(PortName))
                throw new InvalidOperationException("Scale COM port not set. Configure in Settings panel.");

            LastRawLine = ReadWeightFromScale();
            return ParseWeightToLb(LastRawLine);
        }

        /// <summary>
        /// Send tare/zero command to scale
        /// </summary>
        public void TareScale()
        {
            if (_testMode)
                return; // No-op in test mode

            if (string.IsNullOrWhiteSpace(PortName))
                throw new InvalidOperationException("Scale COM port not set.");

            using var sp = CreateSerialPort();
            try
            {
                sp.Open();

                // Try common tare commands
                var tareCommands = new[] { "T\r", "TARE\r", "Z\r", "ZERO\r" };

                foreach (var cmd in tareCommands)
                {
                    try
                    {
                        sp.Write(cmd);
                        Thread.Sleep(100); // Give scale time to process

                        // Read and discard any response
                        try
                        {
                            sp.ReadExisting();
                        }
                        catch (TimeoutException) { }

                        LastRawLine = $"Tare command sent: {cmd.TrimEnd()}";
                        return; // Success
                    }
                    catch
                    {
                        continue; // Try next command
                    }
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Tare failed on {PortName}: {ex.Message}", ex);
            }
        }

        private string ReadWeightFromScale()
        {
            using var sp = CreateSerialPort();

            try
            {
                sp.Open();

                // Auto-detect scale mode or use specified mode
                if (Mode == ScaleMode.Auto)
                {
                    return TryAutoDetectAndRead(sp);
                }
                else if (Mode == ScaleMode.Poll)
                {
                    return PollScaleReading(sp);
                }
                else
                {
                    return ReadContinuousOutput(sp);
                }
            }
            catch (TimeoutException)
            {
                throw new TimeoutException(
                    $"Scale timeout on {PortName}. Check: COM port, baud rate ({BaudRate}), scale power, and RS232 connection.");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Scale communication failed on {PortName}: {ex.Message}", ex);
            }
        }

        private SerialPort CreateSerialPort()
        {
            return new SerialPort
            {
                PortName = PortName.Trim(),
                BaudRate = BaudRate,
                Parity = Parity,
                DataBits = DataBits,
                StopBits = StopBits,
                Handshake = Handshake.None,
                Encoding = Encoding.ASCII,
                ReadTimeout = ReadTimeoutMs,
                WriteTimeout = WriteTimeoutMs,
                NewLine = NewLine
            };
        }

        private string TryAutoDetectAndRead(SerialPort sp)
        {
            // First try continuous mode (wait for data)
            try
            {
                sp.ReadTimeout = 800; // Shorter timeout for auto-detect
                string line = sp.ReadLine();
                if (!string.IsNullOrWhiteSpace(line))
                {
                    Mode = ScaleMode.Continuous; // Remember for next time
                    return line.Trim();
                }
            }
            catch (TimeoutException) { }

            // Try polling mode (send commands)
            try
            {
                sp.ReadTimeout = ReadTimeoutMs; // Restore full timeout
                string result = PollScaleReading(sp);
                Mode = ScaleMode.Poll; // Remember for next time
                return result;
            }
            catch
            {
                throw new TimeoutException("Could not communicate with scale in either continuous or poll mode.");
            }
        }

        private string PollScaleReading(SerialPort sp)
        {
            // Proven scale protocols from forum research and VbScalesReader patterns
            var protocols = new[]
            {
                // METTLER/VEVOR Protocol (most common for VEVOR KF-H2C)
                new ScaleProtocol { Name = "METTLER", Commands = new[] { "\x02P\r", "W\r", "P\r" }, Delay = 100 },
                
                // Generic Protocol
                new ScaleProtocol { Name = "Generic", Commands = new[] { "W\r", "\x05", "?\r" }, Delay = 50 },
                
                // VEVOR Direct Print
                new ScaleProtocol { Name = "VEVOR", Commands = new[] { "PRINT\r", "PRT\r", "\r" }, Delay = 150 },
                
                // CAS Protocol (from VbScalesReader)
                new ScaleProtocol { Name = "CAS", Commands = new[] { "Q\r", "W\r" }, Delay = 75 }
            };

            foreach (var protocol in protocols)
            {
                foreach (var cmd in protocol.Commands)
                {
                    try
                    {
                        // Clear buffer before sending command
                        sp.DiscardInBuffer();

                        // Send command (handle escape sequences)
                        var bytes = System.Text.Encoding.ASCII.GetBytes(cmd.Replace("\\x02", "\x02").Replace("\\x05", "\x05"));
                        sp.Write(bytes, 0, bytes.Length);

                        // Protocol-specific delay
                        Thread.Sleep(protocol.Delay);

                        // Try to read response
                        string response = ReadScaleResponse(sp);
                        if (!string.IsNullOrWhiteSpace(response) && ContainsWeight(response))
                        {
                            LastRawLine = $"Protocol: {protocol.Name}, Command: {FormatCommand(cmd)}, Response: {response}";
                            return response.Trim();
                        }
                    }
                    catch (TimeoutException)
                    {
                        continue; // Try next command
                    }
                }
            }

            throw new TimeoutException($"No valid weight response from scale after trying {protocols.Length} protocols with multiple commands");
        }

        private class ScaleProtocol
        {
            public string Name { get; set; }
            public string[] Commands { get; set; }
            public int Delay { get; set; }
        }

        private string ReadScaleResponse(SerialPort sp)
        {
            // Try multiple read strategies
            try
            {
                // Strategy 1: ReadLine (most common)
                return sp.ReadLine();
            }
            catch (TimeoutException)
            {
                // Strategy 2: ReadExisting (for scales that don't send line endings)
                Thread.Sleep(100);
                var existing = sp.ReadExisting();
                if (!string.IsNullOrEmpty(existing))
                {
                    return existing;
                }
                throw;
            }
        }

        private string FormatCommand(string cmd)
        {
            return cmd.Replace("\x02", "STX").Replace("\x05", "ENQ").Replace("\r", "CR").Replace("\n", "LF");
        }

        private string ReadContinuousOutput(SerialPort sp)
        {
            // Wait for scale to output data
            string line = sp.ReadLine();
            return (line ?? "").Trim();
        }

        private bool ContainsWeight(string line)
        {
            // Quick check if line contains weight-like data
            return Regex.IsMatch(line, @"\d+(\.\d+)?\s*(lb|kg|g|oz)?", RegexOptions.IgnoreCase);
        }

        /// <summary>
        /// Enhanced parser supporting proven scale response formats from research
        /// </summary>
        private double ParseWeightToLb(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                throw new FormatException("Scale output was empty.");

            string s = raw.Trim();

            // Handle proven scale response formats from forum research:
            // METTLER: "S S     1.234 lb"
            // VEVOR: "ST,GS,    2.345 kg" 
            // CAS: " +001.234kg"
            // Generic: "   1.23 lb"
            // DELMAC: "W   1.234 kg"

            var patterns = new[]
            {
                // METTLER Toledo format: "S S     1.234 lb" or "<?> S S 0.000 lb"
                @"(?:[?<>]*\s*)?S\s+S\s+([+-]?\d+(?:\.\d+)?)\s*(lb|lbs|kg|g|oz)?\b",
                
                // VEVOR/Elicom format: "ST,GS,    +2.345 kg"
                @"(?:ST|GS|Net)[\s,]+([+-]?\d+(?:\.\d+)?)\s*(lb|lbs|kg|g|oz)\b",
                
                // CAS format: " +001.234kg" or "001.234kg"
                @"^\s*([+-]?\d+(?:\.\d+)?)(lb|lbs|kg|g|oz)?\s*$",
                
                // DELMAC format: "W   1.234 kg"
                @"W\s+([+-]?\d+(?:\.\d+)?)\s*(lb|lbs|kg|g|oz)\b",
                
                // Generic with label: "WT: 1.23 lb", "Weight: 1.23 kg"  
                @"(?:WT|Weight|Wt):\s*([+-]?\d+(?:\.\d+)?)\s*(lb|lbs|kg|g|oz)?\b",
                
                // Simple format: "1.234 kg" or "  0.560 lb"
                @"([+-]?\d+(?:\.\d+)?)\s*(lb|lbs|kg|g|gram|grams|oz)\b",
                
                // Just number (assume current scale unit - typically lb in US)
                @"^\s*([+-]?\d+(?:\.\d+)?)\s*$"
            };

            foreach (var pattern in patterns)
            {
                var match = Regex.Match(s, pattern, RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    if (double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                    {
                        string unit = match.Groups.Count > 2 && match.Groups[2].Success
                            ? match.Groups[2].Value.Trim().ToLowerInvariant()
                            : "";

                        // Convert to pounds
                        return ConvertToPounds(value, unit);
                    }
                }
            }

            throw new FormatException($"Could not parse weight from scale response: '{raw}'\n" +
                                    "Expected formats: 'S S 1.23 lb', 'ST,GS, 2.34 kg', '1.23kg', 'W 0.56 lb', etc.");
        }

        private double ConvertToPounds(double value, string unit)
        {
            switch (unit)
            {
                case "":
                case "lb":
                case "lbs":
                    return value;

                case "kg":
                    return value * 2.2046226218;

                case "g":
                case "gram":
                case "grams":
                    return value * 0.0022046226218;

                case "oz":
                    return value / 16.0;

                default:
                    throw new FormatException($"Unknown weight unit: '{unit}'");
            }
        }

        /// <summary>
        /// Test scale connection without capturing weight
        /// </summary>
        public bool TestConnection()
        {
            if (_testMode)
                return true;

            try
            {
                CaptureWeightLbOnce();
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}