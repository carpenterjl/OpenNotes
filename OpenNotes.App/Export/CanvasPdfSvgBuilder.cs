using System.Globalization;
using System.Text;
using OpenNotes.Interfaces;
using OpenNotes.Models;
using OpenNotes.Services;
using OpenNotes.ViewModels.Canvas;

namespace OpenNotes.Export;

/// <summary>Builds the two vector layers of a canvas PDF page as SVG strings (rendered natively
/// by QuestPDF, so everything here stays vector in the PDF). The chrome layer holds the page
/// background, connectors, and node shape chrome; the ink layer holds the stroke paths and sits
/// above the raster/text layers, mirroring the on-screen InkCanvas overlay. Both use a viewBox
/// in raw canvas coordinates so no coordinate math leaks into the path data.</summary>
public static class CanvasPdfSvgBuilder
{
    public static string BuildChrome(CanvasPdfPageModel page, CanvasPdfTheme theme)
    {
        var sb = OpenSvg(page);
        sb.Append(
            $"<rect x=\"{N(page.CropX)}\" y=\"{N(page.CropY)}\" width=\"{N(page.CropWidth)}\" " +
            $"height=\"{N(page.CropHeight)}\" fill=\"{Rgb(theme.PageBackgroundHex)}\"/>");

        // Connectors first — they render behind the nodes, as on screen.
        var byId = page.Nodes.ToDictionary(n => n.Id);
        foreach (var c in page.Connectors)
        {
            if (!byId.TryGetValue(c.SourceNodeId, out var s) || !byId.TryGetValue(c.TargetNodeId, out var t))
                continue;
            AppendConnector(sb, c, s, t);
        }

        foreach (var node in page.Nodes)
            AppendShapeChrome(sb, node, theme);

        sb.Append("</svg>");
        return sb.ToString();
    }

    public static string BuildInk(CanvasPdfPageModel page)
    {
        var sb = OpenSvg(page);
        foreach (var stroke in page.InkStrokes)
        {
            if (stroke.Points.Count == 0) continue;
            var path = new StringBuilder();
            path.Append($"M {N(stroke.Points[0].X)} {N(stroke.Points[0].Y)}");
            for (int i = 1; i < stroke.Points.Count; i++)
                path.Append($" L {N(stroke.Points[i].X)} {N(stroke.Points[i].Y)}");
            // A single-point tap still needs a tiny segment or nothing is drawn.
            if (stroke.Points.Count == 1)
                path.Append($" L {N(stroke.Points[0].X + 0.1)} {N(stroke.Points[0].Y)}");

            var cap = stroke.IsHighlighter ? "butt" : "round";
            sb.Append(
                $"<path d=\"{path}\" fill=\"none\" stroke=\"{Rgb(stroke.ColorHex)}\" " +
                $"stroke-width=\"{N(stroke.Width)}\" stroke-opacity=\"{N(stroke.Opacity)}\" " +
                $"stroke-linecap=\"{cap}\" stroke-linejoin=\"round\"/>");
        }
        sb.Append("</svg>");
        return sb.ToString();
    }

    private static StringBuilder OpenSvg(CanvasPdfPageModel page)
    {
        var sb = new StringBuilder();
        sb.Append(
            $"<svg xmlns=\"http://www.w3.org/2000/svg\" " +
            $"viewBox=\"{N(page.CropX)} {N(page.CropY)} {N(page.CropWidth)} {N(page.CropHeight)}\">");
        return sb;
    }

    private static void AppendConnector(StringBuilder sb, DiagramConnector c, DiagramNode s, DiagramNode t)
    {
        var (sx, sy) = CanvasConnectorViewModel.BorderPoint(
            s.X + s.Width / 2, s.Y + s.Height / 2, s.Width, s.Height, t.X + t.Width / 2, t.Y + t.Height / 2);
        var (ex, ey) = CanvasConnectorViewModel.BorderPoint(
            t.X + t.Width / 2, t.Y + t.Height / 2, t.Width, t.Height, s.X + s.Width / 2, s.Y + s.Height / 2);

        // WPF StrokeDashArray units are multiples of the thickness; SVG dasharray is absolute.
        var dash = c.Style switch
        {
            ConnectorStyle.Dashed => $" stroke-dasharray=\"{N(4 * c.Thickness)} {N(2 * c.Thickness)}\"",
            ConnectorStyle.Dotted => $" stroke-dasharray=\"{N(1 * c.Thickness)} {N(2 * c.Thickness)}\"",
            _ => string.Empty,
        };

        sb.Append(
            $"<line x1=\"{N(sx)}\" y1=\"{N(sy)}\" x2=\"{N(ex)}\" y2=\"{N(ey)}\" " +
            $"stroke=\"{Rgb(c.Color)}\" stroke-width=\"{N(c.Thickness)}\"{dash}/>");

        // Arrowhead triangle at the target end — same geometry as the live view.
        const double size = 10, spread = 0.4;
        double ang = Math.Atan2(ey - sy, ex - sx);
        double a1 = ang + Math.PI - spread, a2 = ang + Math.PI + spread;
        sb.Append(
            $"<polygon points=\"{N(ex)},{N(ey)} {N(ex + size * Math.Cos(a1))},{N(ey + size * Math.Sin(a1))} " +
            $"{N(ex + size * Math.Cos(a2))},{N(ey + size * Math.Sin(a2))}\" fill=\"{Rgb(c.Color)}\"/>");
    }

    private static void AppendShapeChrome(StringBuilder sb, DiagramNode n, CanvasPdfTheme theme)
    {
        double x = n.X, y = n.Y, w = n.Width, h = n.Height;
        string fill = Rgb(n.FillColor);
        // ShowBorder off ⇒ no stroke, matching the live template's zeroed StrokeThickness.
        string? stroke = n.ShowBorder ? Rgb(n.StrokeColor) : null;
        string? sw = n.ShowBorder ? N(n.StrokeThickness) : null;
        string strokeAttr = stroke is null ? string.Empty : $" stroke=\"{stroke}\" stroke-width=\"{sw}\"";

        switch (n.Shape)
        {
            case NodeShape.Text:
            case NodeShape.Image:
            case NodeShape.Svg:
                return; // no chrome: bare label / raster layer / embedded SVG layer

            case NodeShape.Ellipse:
                sb.Append(
                    $"<ellipse cx=\"{N(x + w / 2)}\" cy=\"{N(y + h / 2)}\" rx=\"{N(w / 2)}\" ry=\"{N(h / 2)}\" " +
                    $"fill=\"{fill}\"{strokeAttr}/>");
                return;

            case NodeShape.Diamond:
                sb.Append(
                    $"<polygon points=\"{N(x + w / 2)},{N(y)} {N(x + w)},{N(y + h / 2)} " +
                    $"{N(x + w / 2)},{N(y + h)} {N(x)},{N(y + h / 2)}\" " +
                    $"fill=\"{fill}\"{strokeAttr}/>");
                return;

            case NodeShape.StickyNote:
                AppendRect(sb, x, y, w, h, 2, Rgb(theme.StickyFillHex),
                    n.ShowBorder ? Rgb(theme.StickyStrokeHex) : null, "1");
                return;

            case NodeShape.TaskLink:
                AppendRect(sb, x, y, w, h, 4, "#FFF4CE", n.ShowBorder ? "#E0C060" : null, "1");
                return;

            case NodeShape.Latex:
            case NodeShape.Checklist:
                AppendRect(sb, x, y, w, h, 3, Rgb(theme.SurfaceHex), null, null);
                return;

            default: // Rectangle, RoundedRectangle, Container, and shapes the editor can't create yet
                AppendRect(sb, x, y, w, h, n.Shape == NodeShape.RoundedRectangle ? 8 : 4, fill,
                    stroke, sw);
                return;
        }
    }

    private static void AppendRect(
        StringBuilder sb, double x, double y, double w, double h, double rx,
        string fill, string? stroke, string? strokeWidth)
    {
        var strokeAttr = stroke is null ? string.Empty : $" stroke=\"{stroke}\" stroke-width=\"{strokeWidth}\"";
        sb.Append(
            $"<rect x=\"{N(x)}\" y=\"{N(y)}\" width=\"{N(w)}\" height=\"{N(h)}\" rx=\"{N(rx)}\" " +
            $"fill=\"{fill}\"{strokeAttr}/>");
    }

    /// <summary>Normalize any WPF-parsable color string (named, #RGB, #AARRGGBB, …) to #RRGGBB —
    /// SVG's 8-digit hex has the alpha at the END, so WPF hex must never be emitted verbatim.</summary>
    private static string Rgb(string color) =>
        CanvasThemeService.TryParseColor(color, out var c)
            ? $"#{c.R:X2}{c.G:X2}{c.B:X2}"
            : "#000000";

    private static string N(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
