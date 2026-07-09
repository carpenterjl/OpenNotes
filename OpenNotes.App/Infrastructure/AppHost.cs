using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace OpenNotes.Infrastructure;

public static class AppHost
{
    private static bool _loggingConfigured;

    /// <summary>Configure Serilog. Split from <see cref="Build"/> so App.OnStartup can log
    /// breadcrumbs (and the single-instance guard outcome) before the host is built — a startup
    /// stall then pinpoints its exact stage in the log. Idempotent.</summary>
    public static void ConfigureLogging()
    {
        if (_loggingConfigured) return;
        _loggingConfigured = true;

        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "OpenNotes", "logs", "app-.log");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(logPath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
    }

    public static IHost Build()
    {
        ConfigureLogging();

        return Host.CreateDefaultBuilder()
            .UseSerilog()
            .ConfigureServices((_, services) =>
            {
                services.AddOpenNotesServices();
            })
            .Build();
    }
}
