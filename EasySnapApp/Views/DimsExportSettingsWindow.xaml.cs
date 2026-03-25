using System;
using System.Windows;
using EasySnapApp.Models;

namespace EasySnapApp.Views
{
    public partial class DimsExportSettingsWindow : Window
    {
        public DimsExportSettingsWindow()
        {
            InitializeComponent();
            LoadDimsSettings();
        }

        /// <summary>
        /// Load DIMS settings from Properties.Settings into UI controls
        /// </summary>
        private void LoadDimsSettings()
        {
            try
            {
                var settings = DimsExportSettings.Load();

                chkEnableDimsOverride.IsChecked = settings.EnableOverride;
                txtSiteId.Text = settings.SiteId;
                txtFactor.Text = settings.Factor;
                txtDimUnit.Text = settings.DimUnit;
                txtWgtUnit.Text = settings.WgtUnit;
                txtVolUnit.Text = settings.VolUnit;
                txtOptInfo1.Text = settings.OptInfo1;
                txtOptInfo2.Text = settings.OptInfo2;
                txtOptInfo3.Text = settings.OptInfo3;
                txtOptInfo4.Text = settings.OptInfo4;
                txtOptInfo5.Text = settings.OptInfo5;
                txtOptInfo6.Text = settings.OptInfo6;
                txtOptInfo7.Text = settings.OptInfo7;
                txtOptInfo8.Text = settings.OptInfo8;
                txtUpdated.Text = settings.Updated;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load DIMS settings: {ex.Message}", "Settings Error", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                ResetDimsToDefaults();
            }
        }

        /// <summary>
        /// Save DIMS settings from UI controls to Properties.Settings
        /// </summary>
        private void SaveDimsSettings_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var settings = new DimsExportSettings
                {
                    EnableOverride = chkEnableDimsOverride.IsChecked == true,
                    SiteId = txtSiteId.Text?.Trim() ?? "733",
                    Factor = txtFactor.Text?.Trim() ?? "166",
                    DimUnit = txtDimUnit.Text?.Trim() ?? "in",
                    WgtUnit = txtWgtUnit.Text?.Trim() ?? "lb",
                    VolUnit = txtVolUnit.Text?.Trim() ?? "in",
                    OptInfo1 = txtOptInfo1.Text?.Trim() ?? "",
                    OptInfo2 = txtOptInfo2.Text?.Trim() ?? "Y",
                    OptInfo3 = txtOptInfo3.Text?.Trim() ?? "N",
                    OptInfo4 = txtOptInfo4.Text?.Trim() ?? "",
                    OptInfo5 = txtOptInfo5.Text?.Trim() ?? "",
                    OptInfo6 = txtOptInfo6.Text?.Trim() ?? "",
                    OptInfo7 = txtOptInfo7.Text?.Trim() ?? "",
                    OptInfo8 = txtOptInfo8.Text?.Trim() ?? "0",
                    Updated = txtUpdated.Text?.Trim() ?? "N"
                };

                settings.Save();
                
                MessageBox.Show("DIMS export settings saved successfully.", "Settings Saved", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
                
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save DIMS settings: {ex.Message}", "Save Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Reset DIMS settings to default values
        /// </summary>
        private void ResetDimsDefaults_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "Reset all DIMS export settings to default values?", 
                "Reset to Defaults", 
                MessageBoxButton.YesNo, 
                MessageBoxImage.Question);
                
            if (result == MessageBoxResult.Yes)
            {
                ResetDimsToDefaults();
            }
        }

        /// <summary>
        /// Reset UI controls to default DIMS values
        /// </summary>
        private void ResetDimsToDefaults()
        {
            chkEnableDimsOverride.IsChecked = true;
            txtSiteId.Text = "733";
            txtFactor.Text = "166";
            txtDimUnit.Text = "in";
            txtWgtUnit.Text = "lb";
            txtVolUnit.Text = "in";
            txtOptInfo1.Text = "";
            txtOptInfo2.Text = "Y";
            txtOptInfo3.Text = "N";
            txtOptInfo4.Text = "";
            txtOptInfo5.Text = "";
            txtOptInfo6.Text = "";
            txtOptInfo7.Text = "";
            txtOptInfo8.Text = "0";
            txtUpdated.Text = "N";
        }

        /// <summary>
        /// Cancel without saving
        /// </summary>
        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
