using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Highlighting;

namespace OpenNotes.Services;

/// <summary>Maps OpenNotes language identifiers onto AvalonEdit's built-in highlighting
/// definitions and adapts their token colors to the active theme. Stock AvalonEdit palettes
/// are designed for white backgrounds (keywords #0000FF, strings #A31515, …) and become
/// unreadable on a dark surface, so on dark themes each too-dark foreground is lightened
/// while preserving its hue. The originals are memoized per definition so switching back
/// to a light theme restores the stock colors exactly (definitions are process-global).</summary>
public static class CodeHighlighting
{
    private static readonly object _lock = new();

    // definition → (color name → stock foreground color, null = inherited)
    private static readonly Dictionary<string, Dictionary<string, Color?>> _stockColors = new();

    // Which brightness mode each definition currently has applied, to skip redundant work.
    private static readonly Dictionary<string, bool> _appliedDark = new();

    /// <summary>Resolve a language id (csharp, python, cpp, …) to an AvalonEdit definition.
    /// Related languages fall back to the closest built-in grammar; plaintext returns null.</summary>
    public static IHighlightingDefinition? GetDefinition(string? language)
    {
        var names = language?.Trim().ToLowerInvariant() switch
        {
            "csharp" or "c#" or "cs" => new[] { "C#" },
            "javascript" or "js" => new[] { "JavaScript" },
            "typescript" or "ts" => new[] { "JavaScript" },
            "json" => new[] { "Json", "JavaScript" },
            "python" or "py" => new[] { "Python" },
            "java" => new[] { "Java", "C#" },
            "cpp" or "c++" or "c" or "h" => new[] { "C++" },
            "rust" or "go" => new[] { "C++" },        // C-family keywords/strings/comments still help
            "sql" => new[] { "TSQL" },
            "xml" or "xaml" or "svg" => new[] { "XML" },
            "html" or "htm" => new[] { "HTML" },
            "css" => new[] { "CSS" },
            "php" => new[] { "PHP" },
            "vb" or "vbnet" => new[] { "VB" },
            "powershell" or "ps1" => new[] { "PowerShell", "Boo" },
            "bash" or "sh" or "shell" => new[] { "PowerShell", "Boo" },
            "yaml" or "yml" => new[] { "Boo" },       // '#' line comments at least color correctly
            "markdown" or "md" => new[] { "MarkDown" },
            "tex" or "latex" => new[] { "TeX" },
            _ => null,
        };
        if (names is null) return null;

        foreach (var name in names)
        {
            if (HighlightingManager.Instance.GetDefinition(name) is { } def)
                return def;
        }
        return null;
    }

    /// <summary>True when the active app theme has a dark background (drives token-color
    /// adaptation). Defaults to false when no Application resources exist (unit tests).</summary>
    public static bool IsDarkTheme()
    {
        var brush = Application.Current?.TryFindResource("BackgroundBrush") as SolidColorBrush;
        return brush is not null && Luminance(brush.Color) < 0.5;
    }

    /// <summary>Retint a definition's named colors for the requested background. Idempotent —
    /// calling it repeatedly with the same mode is a no-op, and light mode restores stock colors.</summary>
    public static void ApplyTheme(IHighlightingDefinition? definition, bool darkBackground)
    {
        if (definition is null) return;
        lock (_lock)
        {
            if (_appliedDark.TryGetValue(definition.Name, out var applied) && applied == darkBackground)
                return;
            _appliedDark[definition.Name] = darkBackground;

            if (!_stockColors.TryGetValue(definition.Name, out var stock))
            {
                stock = definition.NamedHighlightingColors.ToDictionary(
                    c => c.Name,
                    c => c.Foreground?.GetColor(null));
                _stockColors[definition.Name] = stock;
            }

            foreach (var color in definition.NamedHighlightingColors)
            {
                if (!stock.TryGetValue(color.Name, out var original) || original is not { } stockColor)
                    continue;
                color.Foreground = new SimpleHighlightingBrush(
                    darkBackground ? LightenForDark(stockColor) : stockColor);
            }
        }
    }

    /// <summary>The definition's original (stock) foreground for a highlighting color, regardless
    /// of any dark-theme retint currently applied to the shared definition instance. Offline
    /// renderers (code → SVG snapshots) use this so their colors depend only on their own card
    /// background, not on whichever app theme happens to be active.</summary>
    public static Color? GetStockForeground(IHighlightingDefinition definition, HighlightingColor color)
    {
        lock (_lock)
        {
            if (color.Name is not null &&
                _stockColors.TryGetValue(definition.Name, out var stock) &&
                stock.TryGetValue(color.Name, out var original))
                return original;
        }
        return color.Foreground?.GetColor(null);
    }

    /// <summary>Adapt a light-background token color for a dark surface: keep the hue,
    /// raise the brightness floor so nothing disappears into the background.</summary>
    public static Color LightenForDark(Color c)
    {
        if (Luminance(c) >= 0.55) return c; // already bright enough
        RgbToHsl(c, out var h, out var s, out var l);
        l = Math.Max(l, 0.68);
        s = Math.Min(s, 0.85); // fully saturated primaries look neon on dark; soften slightly
        return HslToRgb(h, s, l, c.A);
    }

    private static double Luminance(Color c)
        => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;

    private static void RgbToHsl(Color c, out double h, out double s, out double l)
    {
        double r = c.R / 255.0, g = c.G / 255.0, b = c.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        l = (max + min) / 2.0;
        if (Math.Abs(max - min) < 0.0001) { h = 0; s = 0; return; }
        var d = max - min;
        s = l > 0.5 ? d / (2.0 - max - min) : d / (max + min);
        if (max == r) h = (g - b) / d + (g < b ? 6 : 0);
        else if (max == g) h = (b - r) / d + 2;
        else h = (r - g) / d + 4;
        h /= 6.0;
    }

    private static Color HslToRgb(double h, double s, double l, byte alpha)
    {
        double Hue(double p, double q, double t)
        {
            if (t < 0) t += 1;
            if (t > 1) t -= 1;
            if (t < 1.0 / 6) return p + (q - p) * 6 * t;
            if (t < 1.0 / 2) return q;
            if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
            return p;
        }

        if (s < 0.0001)
        {
            var v = (byte)Math.Round(l * 255);
            return Color.FromArgb(alpha, v, v, v);
        }

        var qq = l < 0.5 ? l * (1 + s) : l + s - l * s;
        var pp = 2 * l - qq;
        return Color.FromArgb(alpha,
            (byte)Math.Round(Hue(pp, qq, h + 1.0 / 3) * 255),
            (byte)Math.Round(Hue(pp, qq, h) * 255),
            (byte)Math.Round(Hue(pp, qq, h - 1.0 / 3) * 255));
    }
}
