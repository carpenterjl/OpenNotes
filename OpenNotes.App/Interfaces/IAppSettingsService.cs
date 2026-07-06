using OpenNotes.Models;

namespace OpenNotes.Interfaces;

/// <summary>Loads and persists the application-level <see cref="AppSettings"/> (theme choice,
/// custom-theme colors, …) at <c>%APPDATA%\OpenNotes\app-settings.json</c>. Loaded eagerly so the
/// saved theme can be restored during startup; mutate <see cref="Current"/> then call
/// <see cref="SaveAsync"/>.</summary>
public interface IAppSettingsService
{
    /// <summary>The live settings instance (loaded from disk, or defaults if none). Edit in place,
    /// then <see cref="SaveAsync"/>.</summary>
    AppSettings Current { get; }

    /// <summary>Atomically write <see cref="Current"/> back to disk.</summary>
    Task SaveAsync(CancellationToken ct = default);
}
