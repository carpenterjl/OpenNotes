using Microsoft.Extensions.Logging;
using OpenNotes.Interfaces;
using OpenNotes.Models;

namespace OpenNotes.Services;

/// <summary>See <see cref="IAppSettingsService"/>. Reuses <see cref="IPersistenceService"/> for
/// atomic tmp-then-rename writes and its JSON-corruption recovery. Settings are read eagerly in the
/// constructor (a tiny file) so <c>Current</c> is available for startup theme restore before the
/// window is shown.</summary>
public sealed class AppSettingsService : IAppSettingsService
{
    private readonly IPersistenceService _persistence;
    private readonly string _filePath;

    public AppSettings Current { get; }

    public AppSettingsService(IPersistenceService persistence, ILogger<AppSettingsService> logger)
        : this(persistence, logger, DefaultPath())
    {
    }

    /// <summary>Test seam: point at an explicit settings file instead of the real APPDATA path.</summary>
    public AppSettingsService(IPersistenceService persistence, ILogger<AppSettingsService> logger, string filePath)
    {
        _persistence = persistence;
        _filePath = filePath;

        // Eager, blocking load: the file is a few hundred bytes and this runs on the DI build
        // thread (no WPF sync context), so there is no deadlock risk.
        Current = _persistence.ReadAsync<AppSettings>(_filePath).GetAwaiter().GetResult() ?? new AppSettings();
        logger.LogDebug("Loaded app settings (theme '{Theme}', {CustomColors} custom colors)",
            Current.Theme, Current.CustomThemeColors.Count);
    }

    public Task SaveAsync(CancellationToken ct = default) => _persistence.WriteAsync(_filePath, Current, ct);

    private static string DefaultPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "OpenNotes", "app-settings.json");
}
