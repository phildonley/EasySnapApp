using System;

namespace EasySnapApp.Services
{
    public class ScaleService
    {
        private readonly bool _testMode;

        public ScaleService(bool testMode = false) => _testMode = testMode;

        public double GetWeight()
        {
            if (_testMode)
                return 1.23;  // dummy weight
            throw new NotImplementedException("Scale reading not yet implemented.");
        }
    }
}
