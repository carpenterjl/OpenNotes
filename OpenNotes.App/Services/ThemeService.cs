using System.Windows;
using System.Windows.Media;
using Microsoft.Extensions.Logging;
using OpenNotes.Interfaces;

namespace OpenNotes.Services;

/// <summary>Swaps the merged theme ResourceDictionary (Light/Dark/HighContrast) and hosts the
/// standalone <b>Custom</b> theme: a structural base (<c>CustomTheme.xaml</c>) whose ~16 editable
/// brush keys are shadowed by per-user overrides written as direct
/// <c>Application.Current.Resources</c> entries (a direct entry always beats a merged dictionary in
/// WPF lookup — the same mechanism as <see cref="CanvasThemeService"/>). Custom colors and the
/// selected theme persist via <see cref="IAppSettingsService"/> and are restored at startup.</summary>
public class ThemeService : IThemeService
{
    /// <summary>The editable brush slots of the Custom theme, in editor display order.</summary>
    private static readonly ThemeColorItem[] ColorItems =
    [
        new("BackgroundBrush", "Background"),
        new("SurfaceBrush", "Surface"),
        new("SurfaceElevatedBrush", "Surface (elevated)"),
        new("SidebarBrush", "Sidebar"),
        new("BorderBrush", "Border"),
        new("TextPrimaryBrush", "Text (primary)"),
        new("TextSecondaryBrush", "Text (secondary)"),
        new("TextDisabledBrush", "Text (disabled)"),
        new("TextOnAccentBrush", "Text on accent"),
        new("AccentBrush", "Accent"),
        new("AccentHoverBrush", "Accent (hover)"),
        new("AccentPressedBrush", "Accent (pressed)"),
        new("SuccessBrush", "Success"),
        new("WarningBrush", "Warning"),
        new("ErrorBrush", "Error"),
        new("InfoBrush", "Info"),
    ];

    private const string CustomThemeName = "Custom";

    private readonly ILogger<ThemeService> _logger;
    private readonly IAppSettingsService _settings;
    // Stored as source strings (not Uri) so construction never parses a pack:// URI — that requires
    // the WPF pack scheme, which isn't registered in unit tests. The Uri is built lazily in
    // ApplyTheme, which only runs with a live Application (where pack is registered).
    private readonly Dictionary<string, string> _themeSources = [];
    private string _currentTheme;

    public string CurrentTheme => _currentTheme;
    public IReadOnlyList<string> AvailableThemes => [.. _themeSources.Keys];
    public IReadOnlyList<ThemeColorItem> CustomColorItems => ColorItems;

    public event EventHandler<string>? ThemeChanged;

    public ThemeService(ILogger<ThemeService> logger, IAppSettingsService settings)
    {
        _logger = logger;
        _settings = settings;
        RegisterBuiltInThemes();
        _currentTheme = _themeSources.ContainsKey(settings.Current.Theme) ? settings.Current.Theme : "Dark";
    }

    private void RegisterBuiltInThemes()
    {
        _themeSources["Light"] = "pack://application:,,,/Themes/LightTheme.xaml";
        _themeSources["Dark"] = "pack://application:,,,/Themes/DarkTheme.xaml";
        _themeSources["HighContrast"] = "pack://application:,,,/Themes/HighContrastTheme.xaml";
        _themeSources[CustomThemeName] = "pack://application:,,,/Themes/CustomTheme.xaml";
    }

    public void RegisterTheme(string name, Uri resourceUri)
    {
        _themeSources[name] = resourceUri.OriginalString;
    }

    public void ApplyTheme(string themeName)
    {
        if (!_themeSources.TryGetValue(themeName, out var source))
        {
            _logger.LogWarning("Theme '{Theme}' not registered", themeName);
            return;
        }

        var app = Application.Current;
        if (app is null) return;

        var uri = new Uri(source, UriKind.Absolute);

        // Seed the Custom slots from the OUTGOING theme (before the merged swap) so an unedited
        // Custom starts out identical to whatever the user was just looking at.
        if (themeName == CustomThemeName)
            EnsureCustomSeeded(app);

        var existing = app.Resources.MergedDictionaries
            .FirstOrDefault(d => d.Source?.OriginalString?.Contains("Theme.xaml") == true);
        if (existing is not null)
            app.Resources.MergedDictionaries.Remove(existing);

        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = uri });

        if (themeName == CustomThemeName)
            ApplyCustomOverrides(app);
        else
            ClearCustomOverrides(app);

        _currentTheme = themeName;
        _settings.Current.Theme = themeName;
        PersistSettings();

        ThemeChanged?.Invoke(this, themeName);
        _logger.LogInformation("Applied theme '{Theme}'", themeName);
    }

    public IReadOnlyDictionary<string, string> GetCustomColors() =>
        new Dictionary<string, string>(_settings.Current.CustomThemeColors);

    public void SetCustomColor(string key, string hex)
    {
        if (ColorItems.All(i => i.Key != key))
        {
            _logger.LogWarning("Unknown custom theme key '{Key}'", key);
            return;
        }
        if (!CanvasThemeService.TryParseColor(hex, out _))
        {
            _logger.LogWarning("Ignoring invalid custom theme color '{Hex}' for {Key}", hex, key);
            return;
        }

        var app = Application.Current;
        if (app is not null) EnsureCustomSeeded(app); // fill the other slots before overriding one
        _settings.Current.CustomThemeColors[key] = hex;
        PersistSettings();

        if (_currentTheme == CustomThemeName && app is not null)
            ApplyCustomOverrides(app);
        else
            ApplyTheme(CustomThemeName);
    }

    public void ResetCustom()
    {
        _settings.Current.CustomThemeColors.Clear();
        PersistSettings();

        var app = Application.Current;
        if (_currentTheme == CustomThemeName && app is not null)
            ApplyCustomOverrides(app); // empty overrides → reveals the CustomTheme.xaml base
    }

    public IReadOnlyDictionary<string, string> GetEffectiveCustomColors()
    {
        var overrides = _settings.Current.CustomThemeColors;
        var app = Application.Current;
        var result = new Dictionary<string, string>();
        foreach (var item in ColorItems)
        {
            if (overrides.TryGetValue(item.Key, out var hex))
                result[item.Key] = hex;
            else if (app is not null && ReadBrushHex(app, item.Key) is { } live)
                result[item.Key] = live; // fill unedited slots from the live theme so the export is complete
        }
        return result;
    }

    public void ImportCustomColors(IReadOnlyDictionary<string, string> colors)
    {
        // Keep only entries for a known slot with a parseable hex; unknown keys / bad hex are dropped.
        var valid = new Dictionary<string, string>();
        foreach (var item in ColorItems)
            if (colors.TryGetValue(item.Key, out var hex) && CanvasThemeService.TryParseColor(hex, out _))
                valid[item.Key] = hex;

        if (valid.Count == 0)
        {
            _logger.LogWarning("Import theme: no compatible colors found; theme left unchanged");
            return; // "fall back to no theme change if incompatible"
        }

        // Replace outright (not merge): slots absent from the file resolve to the Custom base default
        // because EnsureCustomSeeded only seeds when the override set is empty.
        _settings.Current.CustomThemeColors = valid;
        PersistSettings();
        ApplyTheme(CustomThemeName);
        _logger.LogInformation("Imported {Count} custom theme color(s)", valid.Count);
    }

    // ----- custom-theme resource shadowing -----

    /// <summary>Populate the Custom overrides from the currently-live brushes if not yet customized,
    /// so switching to Custom (or setting one slot) preserves the rest of the current look.</summary>
    private void EnsureCustomSeeded(Application app)
    {
        var colors = _settings.Current.CustomThemeColors;
        if (colors.Count > 0) return;

        foreach (var item in ColorItems)
            if (ReadBrushHex(app, item.Key) is { } hex)
                colors[item.Key] = hex;
        PersistSettings();
    }

    private void ApplyCustomOverrides(Application app)
    {
        var colors = _settings.Current.CustomThemeColors;
        foreach (var item in ColorItems)
        {
            if (colors.TryGetValue(item.Key, out var hex) && CanvasThemeService.TryParseColor(hex, out var color))
            {
                var brush = new SolidColorBrush(color);
                brush.Freeze();
                app.Resources[item.Key] = brush;
            }
            else
            {
                app.Resources.Remove(item.Key); // reveal the merged CustomTheme.xaml base value
            }
        }
    }

    private static void ClearCustomOverrides(Application app)
    {
        foreach (var item in ColorItems)
            app.Resources.Remove(item.Key);
    }

    /// <summary>Resolve a brush key through the live resource lookup (merged dictionaries included)
    /// and format it as <c>#RRGGBB</c>.</summary>
    private static string? ReadBrushHex(Application app, string key) =>
        app.Resources[key] is SolidColorBrush brush
            ? $"#{brush.Color.R:X2}{brush.Color.G:X2}{brush.Color.B:X2}"
            : null;

    private void PersistSettings()
    {
        // Fire-and-forget: a tiny settings file; a transient write failure only loses the last edit.
        _ = _settings.SaveAsync().ContinueWith(
            t => _logger.LogWarning(t.Exception, "Failed to persist app settings"),
            TaskContinuationOptions.OnlyOnFaulted);
    }
}
