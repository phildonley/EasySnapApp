using System;

namespace EasySnapApp.Services
{
    /// <summary>
    /// EDSDK smoke test - must pass before any camera operations
    /// </summary>
    public static class CanonSdkTest
    {
        /// <summary>
        /// Try to initialize and terminate EDSDK to verify DLL is present and working
        /// </summary>
        /// <param name="error">Error message if test fails</param>
        /// <returns>True if SDK loads successfully</returns>
        public static bool TryInitialize(out string error)
        {
            error = null;
            
            try
            {
                // Test 1: Initialize SDK
                uint initResult = CanonEdSdkNative.EdsInitializeSDK();
                if (initResult != CanonEdSdkNative.EDS_ERR_OK)
                {
                    error = $"EdsInitializeSDK failed: {CanonEdSdkNative.GetErrorDescription(initResult)} (0x{initResult:X8})";
                    return false;
                }
                
                // Test 2: Terminate SDK
                uint termResult = CanonEdSdkNative.EdsTerminateSDK();
                if (termResult != CanonEdSdkNative.EDS_ERR_OK)
                {
                    error = $"EdsTerminateSDK failed: {CanonEdSdkNative.GetErrorDescription(termResult)} (0x{termResult:X8})";
                    return false;
                }
                
                return true;
            }
            catch (System.DllNotFoundException ex)
            {
                error = $"EDSDK.dll not found: {ex.Message}. Check that EDSDK.dll is in the output directory and platform target is x64.";
                return false;
            }
            catch (System.BadImageFormatException ex)
            {
                error = $"EDSDK.dll architecture mismatch: {ex.Message}. Ensure project is built for x64 platform.";
                return false;
            }
            catch (System.EntryPointNotFoundException ex)
            {
                error = $"EDSDK function not found: {ex.Message}. Check EDSDK.dll version compatibility.";
                return false;
            }
            catch (Exception ex)
            {
                error = $"Unexpected EDSDK error: {ex.Message}";
                return false;
            }
        }
    }
}
