using System.Windows;
using EasySnapApp.Views.Settings.Measurement;

namespace EasySnapApp.Views
{
    public partial class MeasurementToolsWindow : Window
    {
        public MeasurementToolsWindow()
        {
            InitializeComponent();
            RightContent.Content = new MeasurementGeneralSettingsControl();
        }

        private void BtnGeneral_Click(object sender, RoutedEventArgs e)
        {
            RightContent.Content = new MeasurementGeneralSettingsControl();
        }

        private void BtnInfrared_Click(object sender, RoutedEventArgs e)
        {
            RightContent.Content = new InfraredCamSettingsControl();
        }

        private void BtnMultiCam_Click(object sender, RoutedEventArgs e)
        {
            RightContent.Content = new MultiCamSettingsControl();
        }

        private void BtnLaser_Click(object sender, RoutedEventArgs e)
        {
            RightContent.Content = new LaserArraySettingsControl();
        }
    }
}
