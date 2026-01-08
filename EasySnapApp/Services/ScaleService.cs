using System;

namespace EasySnapApp.Services
{
    public class ScaleService
    {
        private readonly bool _testMode;

        public ScaleService(bool testMode = false) => _testMode = testMode;

        /// <summary>
        /// Capture a single weight reading in pounds.
        /// In testMode, returns a dummy value.
        /// In real mode, this will be implemented via serial (RS232 -> USB adapter) later.
        /// </summary>
        public double CaptureWeightLbOnce()
        {
            if (_testMode)
                return 1.23; // dummy weight for testing UI flow

            throw new NotImplementedException("Scale reading not yet implemented.");
        }
    }
}