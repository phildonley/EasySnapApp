using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using EasySnapApp.Services;

namespace EasySnapApp.Views
{
    public partial class DeviceSettingsWindow : Window
    {
        private readonly KinectService _kinect;
        private readonly CanonCameraService _camera;
        private readonly ThermalScannerService _thermal;
        private readonly IntelIntellisenseService _intel;
        private readonly LaserArrayService _laser;

        public DeviceSettingsWindow(
            KinectService k,
            CanonCameraService c,
            ThermalScannerService t,
            IntelIntellisenseService i,
            LaserArrayService l)
        {
            InitializeComponent();

            DeviceList.SelectedIndex = 0;

            _kinect = k; _camera = c; _thermal = t; _intel = i; _laser = l;

            DeviceList.SelectionChanged += DeviceList_SelectionChanged;
            ActionTabs.SelectionChanged += ActionTabs_SelectionChanged;

            LoadViews();
        }

        private void DeviceList_SelectionChanged(object s, SelectionChangedEventArgs e)
        {
            if (DeviceList.SelectedItem is ListBoxItem item && item.Content != null)
                DeviceTitle.Text = item.Content.ToString();
            else
                DeviceTitle.Text = "";

            LoadViews();
        }

        private void ActionTabs_SelectionChanged(object s, SelectionChangedEventArgs e)
        {
            if (e.Source is TabControl) LoadViews();
        }

        private void LoadViews()
        {
            var deviceTag = (DeviceList.SelectedItem as ListBoxItem)?.Tag as string;
            var tabHeader = (ActionTabs.SelectedItem as TabItem)?.Header as string;

            StageContentHost.Content = null;
            CalibContentHost.Content = null;

            if (tabHeader == "Stage Setup")
            {
                if (deviceTag == "Kinect")
                    StageContentHost.Content =
                        new StageSettingsDialog(_kinect) { Owner = this }.Content;
                else
                    StageContentHost.Content = new TextBlock
                    {
                        Text = "Stage Setup not implemented for this device.",
                        Foreground = Brushes.Gray,
                        Margin = new Thickness(12)
                    };
            }
            else if (tabHeader == "Calibration")
            {
                if (deviceTag == "Kinect")
                    CalibContentHost.Content =
                        new CalibrationDialog(_kinect) { Owner = this }.Content;
                else
                    CalibContentHost.Content = new TextBlock
                    {
                        Text = "Calibration not implemented for this device.",
                        Foreground = Brushes.Gray,
                        Margin = new Thickness(12)
                    };
            }
        }
    }
}
