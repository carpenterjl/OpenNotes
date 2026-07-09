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
    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _activateSignal;

    private const string MutexName = @"Local\OpenNotes.SingleInstance";
    private const string ActivateEventName = @"Local\OpenNotes.ShowWindow";

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            AppHost.ConfigureLogging();
            Log.Information("OnStartup: begin (pid {Pid})", Environment.ProcessId);

            // Global handlers for exceptions that bypass DispatcherUnhandledException (non-UI
            // threads / faulted-and-collected tasks) — without these the process can die or linger
            // with no log line and no dialog.
            AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
            TaskScheduler.UnobservedTaskException += (_, args) =>
            {
                Log.Error(args.Exception, "Unobserved task exception");
                args.SetObserved();
            };

            // Single-instance guard: a second launch signals the primary to come to the front and
            // exits. Also prevents two instances contending on %APPDATA% files (a stalled zombie
            // instance previously caused UnauthorizedAccessException on app-settings.json).
            if (!AcquireSingleInstance())
            {
                Log.Information("Second instance detected; signaling primary and exiting");
                Shutdown(0);
                return;
            }

            _host = AppHost.Build();
            Log.Information("OnStartup: host built");
            await _host.StartAsync();

            Log.Information("OnStartup: resolving app settings");
            // Restore the saved theme (and any Custom-theme color overrides) before showing the UI.
            var settings = _host.Services.GetRequiredService<Interfaces.IAppSettingsService>();
            _host.Services.GetRequiredService<Interfaces.IThemeService>().ApplyTheme(settings.Current.Theme);

            Log.Information("OnStartup: showing main window");
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

    /// <summary>Acquire the single-instance mutex. Returns false (after signaling the primary
    /// instance to activate its window) when another instance already owns it.</summary>
    private bool AcquireSingleInstance()
    {
        bool createdNew;
        try
        {
            _singleInstanceMutex = new Mutex(initiallyOwned: true, MutexName, out createdNew);
        }
        catch (AbandonedMutexException)
        {
            // Previous owner died without releasing — we now own it; treat as acquired.
            createdNew = true;
        }

        if (!createdNew)
        {
            try
            {
                if (EventWaitHandle.TryOpenExisting(ActivateEventName, out var existing))
                {
                    using (existing) existing.Set();
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to signal primary instance");
            }
            _singleInstanceMutex?.Dispose();
            _singleInstanceMutex = null;
            return false;
        }

        // Primary instance: listen for activation signals from later launches.
        _activateSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
        var listener = new Thread(() =>
        {
            var signal = _activateSignal;
            while (signal is not null)
            {
                try
                {
                    signal.WaitOne();
                }
                catch (ObjectDisposedException)
                {
                    return; // shutting down
                }
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (MainWindow is null) return;
                    if (MainWindow.WindowState == WindowState.Minimized)
                        MainWindow.WindowState = WindowState.Normal;
                    MainWindow.Show();
                    MainWindow.Activate();
                }));
            }
        })
        { IsBackground = true, Name = "SingleInstanceActivationListener" };
        listener.Start();
        return true;
    }

    private void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var ex = e.ExceptionObject as Exception;
        Log.Fatal(ex, "Unhandled AppDomain exception (IsTerminating={IsTerminating})", e.IsTerminating);
        Log.CloseAndFlush();
        if (e.IsTerminating)
        {
            // Process is dying on an arbitrary thread — best-effort dialog, no dispatcher marshaling.
            try
            {
                MessageBox.Show(
                    $"OpenNotes encountered a fatal error and must close:\n\n{ex?.Message}\n\nSee logs for details.",
                    "Fatal Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            catch { /* nothing more we can do */ }
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            await _host.StopAsync(TimeSpan.FromSeconds(5));
            _host.Dispose();
        }
        _activateSignal?.Dispose();
        _activateSignal = null;
        if (_singleInstanceMutex is not null)
        {
            try { _singleInstanceMutex.ReleaseMutex(); } catch { /* not owned on this thread */ }
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
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
