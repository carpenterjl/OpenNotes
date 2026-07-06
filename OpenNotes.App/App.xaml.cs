using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenNotes.Infrastructure;
using OpenNotes.Views;
using Serilog;

namespace OpenNotes;

public partial class App : Application
{
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            _host = AppHost.Build();
            await _host.StartAsync();

            // Restore the saved theme (and any Custom-theme color overrides) before showing the UI.
            var settings = _host.Services.GetRequiredService<Interfaces.IAppSettingsService>();
            _host.Services.GetRequiredService<Interfaces.IThemeService>().ApplyTheme(settings.Current.Theme);

            var mainWindow = _host.Services.GetRequiredService<MainWindow>();
            MainWindow = mainWindow;
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Fatal error during startup");
            MessageBox.Show(
                $"OpenNotes failed to start:\n\n{ex.Message}\n\nSee logs for details.",
                "Startup Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            await _host.StopAsync(TimeSpan.FromSeconds(5));
            _host.Dispose();
        }
        Log.CloseAndFlush();
        base.OnExit(e);
    }

    private bool _showingError;

    private void App_DispatcherUnhandledException(object sender,
        System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Fatal(e.Exception, "Unhandled UI exception");
        e.Handled = true;

        // Guard against re-entrancy: showing a modal dialog pumps the dispatcher,
        // which can re-run layout and re-throw the same exception, recursing into a
        // stack overflow. Only surface the first error, and do it asynchronously so
        // we are not inside a layout/render callback when the dialog is shown.
        if (_showingError) return;
        _showingError = true;

        Dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                MessageBox.Show(
                    $"An unexpected error occurred:\n\n{e.Exception.Message}\n\nThe application will continue running.",
                    "OpenNotes Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                _showingError = false;
            }
        }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }
}
