using System;

namespace EasySnapApp.Services
{
    /// <summary>
    /// Stub for a laser‐array dimension scanner.
    /// </summary>
    public class LaserArrayService
    {
        public bool IsConnected { get; private set; } = false;

        public void Calibrate()
        {
            // TODO: implement laser calibration
            throw new NotImplementedException();
        }

        public void SetupStage()
        {
            // TODO: implement stage alignment routine
            throw new NotImplementedException();
        }
    }
}
