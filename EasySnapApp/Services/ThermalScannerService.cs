using System;

namespace EasySnapApp.Services
{
    /// <summary>
    /// Stub for a thermal‐scanner device. 
    /// </summary>
    public class ThermalScannerService
    {
        /// <summary>
        /// True when the hardware is detected/initialized.
        /// TODO: set this based on actual SDK connection logic.
        /// </summary>
        public bool IsConnected { get; private set; } = false;

        public void Calibrate()
        {
            // TODO: implement calibration routine
            throw new NotImplementedException();
        }

        public void SetupStage()
        {
            // TODO: implement stage‐setup UI / logic
            throw new NotImplementedException();
        }
    }
}
