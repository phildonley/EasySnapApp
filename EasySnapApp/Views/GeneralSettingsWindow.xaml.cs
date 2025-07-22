using System.Windows;
using EasySnapApp.Views.Settings.General;

namespace EasySnapApp.Views
{
    public partial class GeneralSettingsWindow : Window
    {
        public GeneralSettingsWindow()
        {
            InitializeComponent();
            RightContent.Content = new GeneralFileSettingsControl();
        }

        private void FileSettings_Click(object sender, RoutedEventArgs e)
        {
            RightContent.Content = new GeneralFileSettingsControl();
        }

        private void InterfaceSettings_Click(object sender, RoutedEventArgs e)
        {
            RightContent.Content = new GeneralInterfaceSettingsControl();
        }
    }
}
