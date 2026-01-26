using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Windows;

namespace EasySnapApp
{
    public partial class SplashWindow : Window, INotifyPropertyChanged
    {
        private string _statusText;
        private string _footerText;
        private Uri _splashImage;

        // ---- EASY TO EDIT CONSTANTS ----
        private const string CompanyName = "EasySnap";
        private const string CopyrightTemplate = "© {YEAR} {COMPANY}. All rights reserved.";

        // --------------------------------

        public event PropertyChangedEventHandler PropertyChanged;

        public Uri SplashImage
        {
            get => _splashImage;
            set { _splashImage = value; OnPropertyChanged(); }
        }

        public string StatusText
        {
            get => _statusText;
            set { _statusText = value; OnPropertyChanged(); }
        }

        public string FooterText
        {
            get => _footerText;
            set { _footerText = value; OnPropertyChanged(); }
        }

        public SplashWindow()
        {
            InitializeComponent();
            DataContext = this;

            // Pick a random splash image
            SplashImage = PickRandomSplash();

            // Build footer text (version + copyright)
            var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0.0";
            var year = DateTime.Now.Year.ToString();

            FooterText =
                $"{CopyrightTemplate.Replace("{YEAR}", year).Replace("{COMPANY}", CompanyName)}\n" +
                $"Version {version}\n" +
                $"Starting EasySnap...";

            StatusText = "Initializing...";
        }

        // Call this from startup to update loading text
        public void SetStatus(string message)
        {
            Dispatcher.Invoke(() =>
            {
                StatusText = message;
            });
        }

        // Call this if you want to update footer text dynamically
        public void SetFooter(string message)
        {
            Dispatcher.Invoke(() =>
            {
                FooterText = message;
            });
        }

        private static Uri PickRandomSplash()
        {
            // These must exactly match your filenames in Assets/Splash
            var options = new List<string>
            {
                "EasySnap_Splash1.png",
                "EasySnap_Splash2.png",
                "EasySnap_Splash3.png",
                "EasySnap_Splash4.png",
                "EasySnap_Splash5.png",
            };

            var pick = options[new Random().Next(options.Count)];

            // Resource URI (Build Action = Resource)
            return new Uri($"pack://application:,,,/Assets/Splash/{pick}", UriKind.Absolute);
        }

        private void OnPropertyChanged([CallerMemberName] string name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
