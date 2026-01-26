using System;
using System.Threading.Tasks;
using System.Windows;

namespace EasySnapApp
{
    public partial class App : Application
    {
        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var splash = new SplashWindow();
            splash.Show();

            // Let WPF render the splash before any heavy startup work
            await Task.Delay(100);

            try
            {
                // TODO: Replace these Task.Delay blocks with your real startup calls.
                splash.SetStatus("Loading configuration…");
                await Task.Delay(200);

                splash.SetStatus("Initializing services…");
                await Task.Delay(250);

                splash.SetStatus("Looking for camera (Canon EDSDK)...");
                await Task.Delay(400);

                splash.SetStatus("Looking for scale…");
                await Task.Delay(250);

                splash.SetStatus("Looking for measuring tool…");
                await Task.Delay(200);

                splash.SetStatus("Finalizing UI…");
                await Task.Delay(200);

                // Show main window
                var main = new MainWindow();
                MainWindow = main;
                main.Show();

                // TEMP: keep splash visible long enough to confirm text + status
                await Task.Delay(2400);
            }
            catch (Exception ex)
            {
                // Show something meaningful on the splash if startup fails
                splash.SetStatus("Startup error");
                splash.SetFooter($"Startup failed:\n{ex.Message}");

                await Task.Delay(2500);
                Shutdown(-1);
                return;
            }
            finally
            {
                // Close splash once main window is up (or after error delay)
                if (splash.IsVisible)
                    splash.Close();
            }
        }
    }
}
