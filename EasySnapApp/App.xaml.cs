using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using EasySnapApp.Data;
using EasySnapApp.Utils;

namespace EasySnapApp
{
    public partial class App : Application
    {
        private Mutex _singleInstanceMutex;

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Single instance (prevents 2 copies fighting over camera/DB/native DLLs)
            bool createdNew;
            _singleInstanceMutex = new Mutex(true, "EasySnapApp.SingleInstance", out createdNew);
            if (!createdNew)
            {
                MessageBox.Show("EasySnapApp is already running.", "EasySnapApp",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }

            // Ensure runtime folders + session_log.txt exist
            AppPaths.EnsureRuntimeFolders();

            var splash = new SplashWindow();
            splash.Show();

            await Task.Delay(100);

            try
            {
                splash.SetStatus("Loading configuration…");
                await Task.Delay(200);

                splash.SetStatus("Initializing services…");

                var db = new EasySnapDb();
                db.InitializeDatabase();
                System.Diagnostics.Debug.WriteLine("EasySnap DB initialized at: " + db.DatabasePath);

                await Task.Delay(250);

                splash.SetStatus("Looking for camera (Canon EDSDK)...");
                await Task.Delay(400);

                splash.SetStatus("Looking for scale…");
                await Task.Delay(250);

                splash.SetStatus("Looking for measuring tool…");
                await Task.Delay(200);

                splash.SetStatus("Finalizing UI…");
                await Task.Delay(200);

                var main = new MainWindow();
                MainWindow = main;
                main.Show();

                await Task.Delay(2400);
            }
            catch (Exception ex)
            {
                splash.SetStatus("Startup error");
                splash.SetFooter($"Startup failed:\n{ex.Message}");

                await Task.Delay(2500);
                Shutdown(-1);
                return;
            }
            finally
            {
                if (splash.IsVisible)
                    splash.Close();
            }
        }
    }
}
