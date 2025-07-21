using System;

namespace EasySnapApp.Services
{
    /// <summary>
    /// Stub for an Intel® RealSense / Intellisense depth camera.
    /// </summary>
    public class IntelIntellisenseService
    {
        public bool IsConnected { get; private set; } = false;

        public void Calibrate()
        {
            // TODO: implement calibration via Intel SDK
            throw new NotImplementedException();
        }

        public void SetupStage()
        {
            // TODO: implement any pre‐capture setup
            throw new NotImplementedException();
        }
    }
}
