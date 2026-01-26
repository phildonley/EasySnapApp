using System;

namespace EasySnapApp.Models
{
    /// <summary>
    /// DIMS Export Settings - configurable constants for DIMS CSV export
    /// </summary>
    public class DimsExportSettings
    {
        public string SiteId { get; set; } = "733";
        public string Factor { get; set; } = "166";
        public string DimUnit { get; set; } = "in";
        public string WgtUnit { get; set; } = "lb";
        public string VolUnit { get; set; } = "in";
        public string OptInfo2 { get; set; } = "Y";
        public string OptInfo3 { get; set; } = "Y";
        public string OptInfo8 { get; set; } = "0";
        public string Updated { get; set; } = "N";
        public bool EnableOverride { get; set; } = true;

        /// <summary>
        /// Load DIMS export settings from Properties.Settings
        /// </summary>
        public static DimsExportSettings Load()
        {
            try
            {
                var settings = Properties.Settings.Default;
                return new DimsExportSettings
                {
                    SiteId = settings.DimsSiteId ?? "733",
                    Factor = settings.DimsFactor ?? "166", 
                    DimUnit = settings.DimsDimUnit ?? "in",
                    WgtUnit = settings.DimsWgtUnit ?? "lb",
                    VolUnit = settings.DimsVolUnit ?? "in",
                    OptInfo2 = settings.DimsOptInfo2 ?? "Y",
                    OptInfo3 = settings.DimsOptInfo3 ?? "Y", 
                    OptInfo8 = settings.DimsOptInfo8 ?? "0",
                    Updated = settings.DimsUpdated ?? "N",
                    EnableOverride = settings.DimsEnableOverride
                };
            }
            catch
            {
                // Return defaults if settings load fails
                return new DimsExportSettings();
            }
        }

        /// <summary>
        /// Save DIMS export settings to Properties.Settings
        /// </summary>
        public void Save()
        {
            try
            {
                var settings = Properties.Settings.Default;
                settings.DimsSiteId = SiteId ?? "733";
                settings.DimsFactor = Factor ?? "166";
                settings.DimsDimUnit = DimUnit ?? "in"; 
                settings.DimsWgtUnit = WgtUnit ?? "lb";
                settings.DimsVolUnit = VolUnit ?? "in";
                settings.DimsOptInfo2 = OptInfo2 ?? "Y";
                settings.DimsOptInfo3 = OptInfo3 ?? "Y";
                settings.DimsOptInfo8 = OptInfo8 ?? "0";
                settings.DimsUpdated = Updated ?? "N";
                settings.DimsEnableOverride = EnableOverride;
                settings.Save();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save DIMS settings: {ex.Message}");
            }
        }
    }
}
