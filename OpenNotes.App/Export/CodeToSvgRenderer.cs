using System.Globalization;
using System.Security;
using System.Text;
using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Highlighting;
using OpenNotes.Services;

namespace OpenNotes.Export;

/// <summary>Card colors for a rendered code SVG. Defaults match the light theme so existing
/// snapshots keep their look when no palette is supplied.</summary>
public record CodeSvgPalette(string BackgroundHex, string TextHex, string MutedHex)
{
    public static CodeSvgPalette Default { get; } = new("#F5F5F5", "#1E1E1E", "#888888");

    /// <summary>The palette a canvas document's effective colors produce: surface card, text
    /// foreground, and a text→surface blend for the line-number gutter. Single source for both
    /// the live canvas snapshots and export-time re-renders.</summary>
    public static CodeSvgPalette FromPdfTheme(Interfaces.CanvasPdfTheme theme) =>
        new(theme.SurfaceHex, theme.TextHex, BlendHex(theme.TextHex, theme.SurfaceHex, 0.45));

    /// <summary>Linear RGB blend of two hex colors (<paramref name="weight"/> towards the second).</summary>
    public static string BlendHex(string fromHex, string towardsHex, double weight)
    {
        if (!CanvasThemeService.TryParseColor(fromHex, out var a) ||
            !CanvasThemeService.TryParseColor(towardsHex, out var b))
            return fromHex;
        byte Mix(byte x, byte y) => (byte)Math.Round(x + (y - x) * weight);
        return $"#{Mix(a.R, b.R):X2}{Mix(a.G, b.G):X2}{Mix(a.B, b.B):X2}";
    }
}

/// <summary>Renders a code snippet to a static, self-contained SVG string — no WebView2 or
/// AvalonEdit control. Tokens are syntax-colored with AvalonEdit's offline highlighting engine
/// (<see cref="DocumentHighlighter"/>, no UI involved), automatically brightened when the card
/// background is dark. Line geometry is measured with <see cref="FormattedText"/> so the emitted
/// SVG sizes itself to the content.</summary>
public static class CodeToSvgRenderer
{
    private const double FontSize = 13;
    private const double LineHeight = 18;
    private const double PadX = 12;
    private const double PadY = 10;
    private const double GutterGap = 10;

    private static readonly Typeface MonoFace = new(
        new FontFamily("Consolas, Courier New"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);

    public static string Render(string code, string language, bool showLineNumbers, CodeSvgPalette? palette = null)
    {
        palette ??= CodeSvgPalette.Default;
        var lines = (code ?? string.Empty).Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');

        // Measure the widest line (plus optional gutter) to size the canvas.
        double gutterWidth = 0;
        if (showLineNumbers)
            gutterWidth = MeasureWidth(lines.Length.ToString(CultureInfo.InvariantCulture)) + GutterGap;

        double maxTextWidth = 0;
        foreach (var line in lines)
            maxTextWidth = Math.Max(maxTextWidth, MeasureWidth(line));

        double width = Math.Ceiling(PadX * 2 + gutterWidth + Math.Max(maxTextWidth, 40));
        double height = Math.Ceiling(PadY * 2 + Math.Max(lines.Length, 1) * LineHeight);

        var darkCard = IsDark(palette.BackgroundHex);
        var lineColors = HighlightLines(code ?? string.Empty, language, lines.Length, darkCard);

        var sb = new StringBuilder();
        sb.Append(CultureInfo.InvariantCulture,
            $"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width}\" height=\"{height}\" " +
            $"viewBox=\"0 0 {width} {height}\">");
        sb.Append(CultureInfo.InvariantCulture,
            $"<rect x=\"0\" y=\"0\" width=\"{width}\" height=\"{height}\" rx=\"6\" fill=\"{palette.BackgroundHex}\"/>");

        double textX = PadX + gutterWidth;
        for (int i = 0; i < lines.Length; i++)
        {
            double y = PadY + i * LineHeight + FontSize; // baseline

            if (showLineNumbers)
            {
                sb.Append(CultureInfo.InvariantCulture,
                    $"<text x=\"{PadX}\" y=\"{y}\" font-family=\"Consolas,monospace\" " +
                    $"font-size=\"{FontSize}\" fill=\"{palette.MutedHex}\" xml:space=\"preserve\">{Esc((i + 1).ToString(CultureInfo.InvariantCulture))}</text>");
            }

            AppendLine(sb, lines[i], lineColors?[i], textX, y, palette.TextHex);
        }

        sb.Append("</svg>");
        return sb.ToString();
    }

    /// <summary>Emit one source line as colored segments. <paramref name="colors"/> holds one hex
    /// (or null = default) per character; consecutive equal colors coalesce into one text element
    /// whose x offset is the measured width of the preceding prefix, so segments line up exactly.</summary>
    private static void AppendLine(StringBuilder sb, string line, string?[]? colors, double textX, double y, string defaultHex)
    {
        if (line.Length == 0) return;

        int start = 0;
        while (start < line.Length)
        {
            var color = colors is not null && start < colors.Length ? colors[start] : null;
            int end = start + 1;
            while (end < line.Length &&
                   (colors is not null && end < colors.Length ? colors[end] : null) == color)
                end++;

            var x = textX + (start == 0 ? 0 : MeasureWidth(line[..start]));
            sb.Append(CultureInfo.InvariantCulture,
                $"<text x=\"{x}\" y=\"{y}\" font-family=\"Consolas,monospace\" " +
                $"font-size=\"{FontSize}\" fill=\"{color ?? defaultHex}\" xml:space=\"preserve\">{Esc(line[start..end])}</text>");
            start = end;
        }
    }

    /// <summary>Run AvalonEdit's offline highlighter over the code and produce a per-line,
    /// per-character color map (hex or null for default). Returns null for plaintext or when
    /// highlighting fails — the caller renders monochrome, never breaks the snapshot.</summary>
    private static string?[][]? HighlightLines(string code, string language, int lineCount, bool darkCard)
    {
        try
        {
            var definition = CodeHighlighting.GetDefinition(language);
            if (definition is null) return null;

            var document = new TextDocument(code.Replace("\r\n", "\n").Replace("\r", "\n"));
            var highlighter = new DocumentHighlighter(document, definition);

            var result = new string?[lineCount][];
            for (int i = 0; i < Math.Min(lineCount, document.LineCount); i++)
            {
                var docLine = document.GetLineByNumber(i + 1);
                var colors = new string?[docLine.Length];
                var highlighted = highlighter.HighlightLine(i + 1);
                foreach (var section in highlighted.Sections)
                {
                    if (section.Color is null) continue;
                    if (CodeHighlighting.GetStockForeground(definition, section.Color) is not { } c) continue;
                    if (darkCard) c = CodeHighlighting.LightenForDark(c);
                    var hex = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
                    var from = Math.Max(0, section.Offset - docLine.Offset);
                    var to = Math.Min(docLine.Length, section.Offset + section.Length - docLine.Offset);
                    for (int k = from; k < to; k++) colors[k] = hex;
                }
                result[i] = colors;
            }
            return result;
        }
        catch
        {
            return null; // highlighting must never break a snapshot render
        }
    }

    private static bool IsDark(string hex)
        => Services.CanvasThemeService.TryParseColor(hex, out var c) &&
           (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0 < 0.5;

    private static double MeasureWidth(string text)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        var ft = new FormattedText(
            text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            MonoFace, FontSize, Brushes.Black, 1.0);
        return ft.WidthIncludingTrailingWhitespace;
    }

    private static string Esc(string s) => SecurityElement.Escape(s) ?? string.Empty;
}
