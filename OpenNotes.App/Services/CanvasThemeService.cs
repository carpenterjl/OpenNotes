using System.Windows;
using System.Windows.Media;
using Microsoft.Extensions.Logging;
using OpenNotes.Interfaces;

namespace OpenNotes.Services;

/// <summary>See <see cref="ICanvasThemeService"/>. The three canvas keys below are defined with
/// theme-appropriate defaults in each theme ResourceDictionary (Light/Dark/HighContrast); this
/// service shadows them with per-document overrides via direct <c>Application.Current.Resources</c>
/// entries (a direct entry always wins over merged dictionaries in WPF resource lookup).</summary>
public sealed class CanvasThemeService : ICanvasThemeService
{
    /// <summary>Node labels, checklist text, LaTeX formula foreground.</summary>
    public const string TextKey = "CanvasTextBrush";
    /// <summary>Selection outlines, resize grips, connector selection; Mermaid borders/lines.</summary>
    public const string AccentKey = "CanvasAccentBrush";
    /// <summary>LaTeX/checklist card backgrounds; Mermaid primary (node fill) color.</summary>
    public const string SurfaceKey = "CanvasSurfaceBrush";

    private static readonly string[] Keys = [TextKey, AccentKey, SurfaceKey];

    private readonly ILogger<CanvasThemeService> _logger;
    private readonly Func<ResourceDictionary?> _resources;

    public CanvasThemeService(ILogger<CanvasThemeService> logger)
        : this(logger, static () => Application.Current?.Resources)
    {
    }

    /// <summary>Test seam: operate on an explicit ResourceDictionary instead of the live
    /// <c>Application.Current.Resources</c> (which doesn't exist in unit tests).</summary>
    public CanvasThemeService(ILogger<CanvasThemeService> logger, Func<ResourceDictionary?> resources)
    {
        _logger = logger;
        _resources = resources;
    }

    public IReadOnlyList<string> ThemeKeys => Keys;

    public void Apply(IReadOnlyDictionary<string, string> overrides)
    {
        var resources = _resources();
        if (resources is null) return;

        foreach (var key in Keys)
        {
            if (overrides.TryGetValue(key, out var hex) && TryParseColor(hex, out var color))
            {
                var brush = new SolidColorBrush(color);
                brush.Freeze();
                resources[key] = brush;
            }
            else
            {
                if (hex is not null)
                    _logger.LogWarning("Ignoring invalid canvas theme color '{Hex}' for {Key}", hex, key);
                resources.Remove(key);
            }
        }
    }

    public void Reset() => Apply(new Dictionary<string, string>());

    public string? GetThemeDefaultHex(string key)
    {
        var resources = _resources();
        if (resources is null) return null;

        // Search only the merged (theme) dictionaries: a matching direct entry would be the
        // active override, not the default. Later-merged dictionaries win, so scan backwards.
        for (var i = resources.MergedDictionaries.Count - 1; i >= 0; i--)
        {
            var dict = resources.MergedDictionaries[i];
            if (dict.Contains(key) && dict[key] is SolidColorBrush brush)
                return ToHex(brush.Color);
        }
        return null;
    }

    /// <summary>The effective live value of a brush key as <c>#RRGGBB</c>: a direct
    /// <c>Application.Current.Resources</c> entry (an active app-theme or canvas override) if present,
    /// else the merged theme default. Used to capture PDF colors that reflect the Custom theme.</summary>
    public string? GetEffectiveHex(string key)
    {
        var resources = _resources();
        if (resources is null) return null;

        // ResourceDictionary.Contains checks only this dictionary's own entries (not the merged
        // theme dictionaries), so a hit here is a direct override written by this service or the app
        // Custom theme — which wins in WPF lookup. Otherwise fall back to the merged theme default.
        if (resources.Contains(key) && resources[key] is SolidColorBrush direct)
            return ToHex(direct.Color);
        return GetThemeDefaultHex(key);
    }

    public IReadOnlyDictionary<string, string>? GetMermaidThemeVariables(IReadOnlyDictionary<string, string> overrides)
    {
        // No early-out on empty overrides: Effective() falls back to the app theme's canvas
        // defaults, so Mermaid renders match the current theme even without document overrides.
        string? Effective(string key) =>
            overrides.TryGetValue(key, out var hex) && TryParseColor(hex, out var color)
                ? ToHex(color)
                : GetThemeDefaultHex(key);

        var vars = new Dictionary<string, string>();
        if (Effective(SurfaceKey) is { } surface)
            vars["primaryColor"] = surface;
        if (Effective(TextKey) is { } text)
        {
            vars["primaryTextColor"] = text;
            vars["textColor"] = text;
        }
        if (Effective(AccentKey) is { } accent)
        {
            vars["primaryBorderColor"] = accent;
            vars["lineColor"] = accent;
        }
        return vars.Count > 0 ? vars : null;
    }

    public static bool TryParseColor(string hex, out Color color)
    {
        try
        {
            color = (Color)ColorConverter.ConvertFromString(hex);
            return true;
        }
        catch
        {
            color = default;
            return false;
        }
    }

    private static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
}
