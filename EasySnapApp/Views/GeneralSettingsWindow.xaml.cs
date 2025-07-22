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

        private void BtnFile_Click(object sender, RoutedEventArgs e)
        {
            RightContent.Content = new GeneralFileSettingsControl();
        }

        private void BtnInterface_Click(object sender, RoutedEventArgs e)
        {
            RightContent.Content = new GeneralInterfaceSettingsControl();
        }
    }
}
