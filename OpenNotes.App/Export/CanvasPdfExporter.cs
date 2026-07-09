using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging;
using OpenNotes.Interfaces;
using OpenNotes.Models;
using OpenNotes.Services;
using OpenNotes.ViewModels.Canvas;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using WpfMath.Parsers;
using WpfMath.Rendering;
using XamlMath;

namespace OpenNotes.Export;

/// <summary>High-fidelity canvas → PDF export (QuestPDF). One PDF page per canvas page, cropped
/// to the page's content bounds and scaled to fit A4 landscape (never above natural print size).
/// Hybrid rendering: labels/checklists become selectable PDF text; shapes, connectors, and ink
/// strokes are native vector paths (via the SVG layers from <see cref="CanvasPdfSvgBuilder"/>);
/// LaTeX renders through WpfMath at 300 DPI and Mermaid reuses its captured PNG, both placed as
/// pixel-perfect images. Page models are built on the calling (UI) thread — WPF ink/LaTeX and
/// file reads happen there — so the QuestPDF composition can run on a worker thread.</summary>
public class CanvasPdfExporter : ICanvasPdfExporter
{
    static CanvasPdfExporter() => QuestPDF.Settings.License = LicenseType.Community;

    /// <summary>Content-bounds margin around the cropped drawing, canvas px.</summary>
    private const double CropMargin = 40;

    /// <summary>Markdown-note content inset (matches the live viewer's <c>Margin="6,4"</c>) and its
    /// fixed on-screen font size, so the raster aligns with the sticky rect and reads identically.</summary>
    private const double StickyMargin = 6;
    private const double StickyMarginY = 4;
    private const double StickyFontPx = 12;

    // A4 landscape (842×595 pt) minus page margins, header, and footer.
    private const double AvailableWidthPt = 794;
    private const double AvailableHeightPt = 500;

    /// <summary>Max canvas-px → PDF-pt scale: 0.75 pt/px is exactly 100 % zoom on paper
    /// (96 px/inch → 72 pt/inch), so small diagrams print at natural size instead of ballooning.</summary>
    private const double NaturalScale = 0.75;

    private readonly ILogger<CanvasPdfExporter> _logger;
    private readonly Func<string, string, byte[]?> _renderLatexPng;

    public CanvasPdfExporter(ILogger<CanvasPdfExporter> logger)
        : this(logger, RenderLatexPng)
    {
    }

    /// <summary>Test seam: swap the WpfMath-based LaTeX rasterizer (which needs an STA thread)
    /// for a fake. Mirrors the CanvasThemeService pattern.</summary>
    public CanvasPdfExporter(ILogger<CanvasPdfExporter> logger, Func<string, string, byte[]?> renderLatexPng)
    {
        _logger = logger;
        _renderLatexPng = renderLatexPng;
    }

    public async Task ExportAsync(CanvasDocument document, CanvasPdfTheme theme, string outputPath, CancellationToken ct = default)
    {
        var pages = BuildPageModels(document, theme);
        var title = document.Manifest.Title;

        await Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            Document.Create(container => Compose(container, title, pages, theme)).GeneratePdf(outputPath);
        }, ct);

        _logger.LogInformation("Exported canvas document {Id} ({PageCount} pages) to PDF: {Path}",
            document.Manifest.Id, pages.Count, outputPath);
    }

    // ----- page model building (calling thread: WPF ink/LaTeX + file I/O) -----

    public IReadOnlyList<CanvasPdfPageModel> BuildPageModels(CanvasDocument document, CanvasPdfTheme theme)
    {
        var models = new List<CanvasPdfPageModel>();
        foreach (var page in document.Pages)
        {
            var nodes = page.Diagram.Nodes.ToList();
            var ink = ExtractInk(page, nodes);
            var (x, y, w, h) = ComputeCropBounds(nodes, ink);

            var images = new Dictionary<Guid, byte[]>();
            foreach (var node in nodes)
            {
                var bytes = LoadNodeImage(node, theme);
                if (bytes is not null) images[node.Id] = bytes;
            }

            // Re-render code SVGs with the EXPORT theme (the persisted SvgContent has the colors
            // that were active when the node was captured). Must happen here on the calling (UI)
            // thread — CodeToSvgRenderer measures with WPF FormattedText. The document itself is
            // never mutated; nodes without authored source fall back to their stored SVG.
            var svgs = new Dictionary<Guid, string>();
            foreach (var node in nodes)
            {
                if (node.BlockKind != "code" || string.IsNullOrWhiteSpace(node.AuthoredSource))
                    continue;
                try
                {
                    svgs[node.Id] = CodeToSvgRenderer.Render(
                        node.AuthoredSource, node.AuthoredLanguage ?? "plaintext",
                        node.AuthoredShowLineNumbers, CodeSvgPalette.FromPdfTheme(theme));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Code re-render failed for node {NodeId}; exporting the captured SVG.", node.Id);
                }
            }

            // Pixel-perfect markdown notes: rasterize the live WPF MarkdownViewer (UI thread) so the
            // PDF matches the canvas exactly. Transparent PNG overlaid on the vector sticky rect.
            var markdownImages = new Dictionary<Guid, byte[]>();
            foreach (var node in nodes)
            {
                if (node.Shape != NodeShape.StickyNote || node.BlockKind != "markdown" ||
                    string.IsNullOrWhiteSpace(node.Label))
                    continue;
                var fg = CanvasThemeService.TryParseColor(theme.StickyTextHex, out var c)
                    ? c : System.Windows.Media.Colors.Black;
                var png = MarkdownImageRenderer.Render(
                    node.Label,
                    Math.Max(node.Width - 2 * StickyMargin, 1),
                    Math.Max(node.Height - 2 * StickyMarginY, 1),
                    StickyFontPx, fg);
                if (png is not null) markdownImages[node.Id] = png;
            }

            models.Add(new CanvasPdfPageModel
            {
                Title = page.Info.Title,
                CropX = x, CropY = y, CropWidth = w, CropHeight = h,
                Nodes = nodes,
                Connectors = page.Diagram.Connectors.ToList(),
                NodeImages = images,
                NodeSvgs = svgs,
                NodeMarkdownImages = markdownImages,
                InkStrokes = ink,
            });
        }
        return models;
    }

    private byte[]? LoadNodeImage(DiagramNode node, CanvasPdfTheme theme)
    {
        try
        {
            if (node.Shape == NodeShape.Image && !string.IsNullOrWhiteSpace(node.ImagePath))
                return File.Exists(node.ImagePath) ? File.ReadAllBytes(node.ImagePath) : null;

            if (node.Shape == NodeShape.Latex && !string.IsNullOrWhiteSpace(node.LatexContent))
            {
                // 1) The node's cached KaTeX PNG — full LaTeX support (bold, \boxed, \mathbb),
                //    4x supersampled, already colored with the effective canvas text color, and
                //    pixel-identical to the on-screen node by construction.
                if (!string.IsNullOrWhiteSpace(node.ImagePath) && File.Exists(node.ImagePath))
                    return File.ReadAllBytes(node.ImagePath);

                // 2) WpfMath raster (limited dialect) — kept as the offline/STA fallback so an
                //    export never silently loses a formula when no PNG was ever rendered
                //    (WebView2 unavailable, legacy document). 3) Callers fall back to raw text.
                return _renderLatexPng(node.LatexContent, theme.TextHex);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Skipping raster content for canvas node {NodeId} during PDF export.", node.Id);
        }
        return null;
    }

    /// <summary>Flatten the page's floating ink plus every node's bound ink (translated back to
    /// page coordinates) into plain point arrays. Highlighter strokes sort first within each
    /// group so they render underneath regular ink, as WPF draws them.</summary>
    public static List<CanvasPdfInkStroke> ExtractInk(CanvasPage page, IReadOnlyList<DiagramNode> nodes)
    {
        var strokes = new List<CanvasPdfInkStroke>();
        foreach (var node in nodes)
            strokes.AddRange(ToInkStrokes(node.InkData, node.X, node.Y));
        strokes.AddRange(ToInkStrokes(page.FloatingInk, 0, 0));
        return strokes;
    }

    private static IEnumerable<CanvasPdfInkStroke> ToInkStrokes(byte[]? isf, double offsetX, double offsetY)
    {
        return InkSerializer.FromBytes(isf)
            .Select(stroke =>
            {
                var da = stroke.DrawingAttributes;
                var points = stroke.StylusPoints
                    .Select(p => (p.X + offsetX, p.Y + offsetY))
                    .ToList();
                return new CanvasPdfInkStroke(
                    points,
                    $"#{da.Color.R:X2}{da.Color.G:X2}{da.Color.B:X2}",
                    da.IsHighlighter ? 0.45 : da.Color.A / 255.0,
                    Math.Max(da.Width, da.Height),
                    da.IsHighlighter);
            })
            .OrderByDescending(s => s.IsHighlighter);
    }

    /// <summary>Bounding box of all nodes and ink plus <see cref="CropMargin"/>; a sensible
    /// default window for an empty page so the export never degenerates.</summary>
    public static (double X, double Y, double Width, double Height) ComputeCropBounds(
        IReadOnlyList<DiagramNode> nodes, IReadOnlyList<CanvasPdfInkStroke> ink)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;

        foreach (var n in nodes)
        {
            minX = Math.Min(minX, n.X); minY = Math.Min(minY, n.Y);
            maxX = Math.Max(maxX, n.X + n.Width); maxY = Math.Max(maxY, n.Y + n.Height);
        }
        foreach (var s in ink)
        {
            double half = s.Width / 2;
            foreach (var (px, py) in s.Points)
            {
                minX = Math.Min(minX, px - half); minY = Math.Min(minY, py - half);
                maxX = Math.Max(maxX, px + half); maxY = Math.Max(maxY, py + half);
            }
        }

        if (minX > maxX) return (0, 0, 800, 500); // empty page
        return (minX - CropMargin, minY - CropMargin,
                maxX - minX + 2 * CropMargin, maxY - minY + 2 * CropMargin);
    }

    // ----- QuestPDF composition (thread-agnostic: plain data in, PDF out) -----

    public static void Compose(
        IDocumentContainer container, string documentTitle,
        IReadOnlyList<CanvasPdfPageModel> pages, CanvasPdfTheme theme)
    {
        foreach (var page in pages)
        {
            container.Page(p =>
            {
                p.Size(PageSizes.A4.Landscape());
                p.Margin(24);
                p.PageColor(theme.PageBackgroundHex);
                p.DefaultTextStyle(t => t
                    .FontFamily("Segoe UI", "Segoe UI Symbol", "Segoe UI Emoji")
                    .FontSize(10)
                    .FontColor(theme.TextHex));

                p.Header().Text(text =>
                {
                    text.Span(documentTitle).SemiBold().FontSize(11);
                    text.Span($"  ·  {page.Title}").FontSize(11).FontColor(theme.AccentHex);
                });

                p.Content().PaddingTop(6).AlignCenter().AlignMiddle()
                    .Element(e => ComposeCanvasPage(e, page, theme));

                p.Footer().AlignRight().Text(text =>
                {
                    text.DefaultTextStyle(s => s.FontSize(8).FontColor(theme.AccentHex));
                    text.CurrentPageNumber();
                    text.Span(" / ");
                    text.TotalPages();
                });
            });
        }
    }

    private static void ComposeCanvasPage(IContainer container, CanvasPdfPageModel page, CanvasPdfTheme theme)
    {
        double s = Math.Min(
            Math.Min(AvailableWidthPt / page.CropWidth, AvailableHeightPt / page.CropHeight),
            NaturalScale);

        container
            .Width((float)(page.CropWidth * s))
            .Height((float)(page.CropHeight * s))
            .Layers(layers =>
            {
                // 1. Vector chrome: background, connectors, node shapes/cards.
                layers.PrimaryLayer().Svg(CanvasPdfSvgBuilder.BuildChrome(page, theme));

                // 2. Per-node content: raster images, embedded SVG snapshots, selectable text.
                foreach (var node in page.Nodes)
                    ComposeNodeContent(layers, node, page, theme, s);

                // 3. Ink strokes — the topmost layer, like the on-screen InkCanvas overlay.
                layers.Layer().Svg(CanvasPdfSvgBuilder.BuildInk(page));
            });
    }

    private static void ComposeNodeContent(
        LayersDescriptor layers, DiagramNode node, CanvasPdfPageModel page, CanvasPdfTheme theme, double s)
    {
        float X(double canvasX) => (float)((canvasX - page.CropX) * s);
        float Y(double canvasY) => (float)((canvasY - page.CropY) * s);

        if (page.NodeImages.TryGetValue(node.Id, out var imageBytes))
        {
            layers.Layer().Unconstrained()
                .OffsetX(X(node.X)).OffsetY(Y(node.Y))
                .Width((float)(node.Width * s)).Height((float)(node.Height * s))
                .Padding((float)(2 * s))
                .AlignCenter().AlignMiddle()
                .Image(imageBytes).FitArea();
            return;
        }

        if (node.Shape == NodeShape.Svg &&
            (page.NodeSvgs.ContainsKey(node.Id) || !string.IsNullOrWhiteSpace(node.SvgContent)))
        {
            // Prefer the export-time re-rendered SVG (theme-fresh code colors); fall back to the
            // captured snapshot for legacy nodes without authored source.
            var svg = page.NodeSvgs.TryGetValue(node.Id, out var rerendered) ? rerendered : node.SvgContent!;

            // Replicate the live template's <Image Stretch="Uniform" Margin="2">: uniform-scale
            // the SVG into the (W-4)×(H-4) content box and CENTER it (letterboxing as WPF does).
            // QuestPDF's .Svg() alone stretches to fill, which shifts every glyph relative to the
            // node box — node-bound ink (anchored to node bounds) then lands on the wrong token.
            // Computing the fit from the SVG actually placed also absorbs any intrinsic-size drift
            // between the captured snapshot and the export-time re-render.
            if (TryGetSvgSize(svg, out var svgW, out var svgH))
            {
                var (fitX, fitY, fitW, fitH) = UniformFitRect(node.X, node.Y, node.Width, node.Height, 2, svgW, svgH);
                layers.Layer().Unconstrained()
                    .OffsetX(X(fitX)).OffsetY(Y(fitY))
                    .Width((float)(fitW * s)).Height((float)(fitH * s))
                    .Svg(svg);
            }
            else
            {
                // Unknown intrinsic size — legacy stretch placement (better than dropping the node).
                layers.Layer().Unconstrained()
                    .OffsetX(X(node.X)).OffsetY(Y(node.Y))
                    .Width((float)(node.Width * s)).Height((float)(node.Height * s))
                    .Padding((float)(2 * s))
                    .Svg(svg);
            }
            return;
        }

        if (node.Shape == NodeShape.StickyNote && node.BlockKind == "markdown" &&
            !string.IsNullOrWhiteSpace(node.Label))
        {
            // Pixel-perfect path: the live MarkdownViewer rasterized to a transparent PNG, overlaid
            // on the vector sticky rect at the same 6/4 inset it uses on screen.
            if (page.NodeMarkdownImages.TryGetValue(node.Id, out var mdPng))
            {
                layers.Layer().Unconstrained()
                    .OffsetX(X(node.X + StickyMargin)).OffsetY(Y(node.Y + StickyMarginY))
                    .Width((float)(Math.Max(node.Width - 2 * StickyMargin, 1) * s))
                    .Height((float)(Math.Max(node.Height - 2 * StickyMarginY, 1) * s))
                    .Image(mdPng).FitArea();
                return;
            }

            // Fallback (raster failed): selectable rich text via the Markdig → QuestPDF composer.
            double mdFontPx = node.FontSize > 0 ? node.FontSize : 12;
            var mdMuted = CodeSvgPalette.BlendHex(theme.StickyTextHex, theme.StickyFillHex, 0.35);
            layers.Layer().Unconstrained()
                .OffsetX(X(node.X + 6)).OffsetY(Y(node.Y + 4))
                .Width((float)Math.Max((node.Width - 12) * s, 8))
                .Element(e => MarkdownPdfComposer.Compose(
                    e, node.Label, (float)(mdFontPx * s), theme.StickyTextHex, mdMuted));
            return;
        }

        if (string.IsNullOrWhiteSpace(node.Label) &&
            !(node.Shape == NodeShape.Latex && !string.IsNullOrWhiteSpace(node.LatexContent)))
            return;

        double fontSizePx = node.FontSize > 0 ? node.FontSize : 12;
        float fontSizePt = (float)(fontSizePx * s);

        if (node.Shape == NodeShape.Checklist)
        {
            // Top-left monospace block, as in the live template.
            layers.Layer().Unconstrained()
                .OffsetX(X(node.X + 6)).OffsetY(Y(node.Y + 4))
                .Width((float)Math.Max((node.Width - 12) * s, 8))
                .Text(node.Label)
                .FontFamily("Consolas", "Courier New", "Segoe UI Symbol")
                .FontSize(fontSizePt)
                .FontColor(theme.TextHex);
            return;
        }

        // LaTeX whose 300-DPI render failed falls back to its raw source as selectable text.
        var label = node.Shape == NodeShape.Latex ? node.LatexContent ?? node.Label : node.Label;

        // Centered label (all remaining shapes center both ways in the live template). Vertical
        // centering is approximated from an estimated wrapped-text height; the layer is
        // unconstrained, so a too-long label spills below the node exactly as WPF renders it.
        double textWidth = Math.Max(node.Width - 8, 10);
        double estHeight = EstimateTextHeight(label, fontSizePx, textWidth);
        double top = node.Y + Math.Max(2, (node.Height - estHeight) / 2);

        layers.Layer().Unconstrained()
            .OffsetX(X(node.X + 4)).OffsetY(Y(top))
            .Width((float)(textWidth * s))
            .Text(t =>
            {
                t.AlignCenter();
                t.Span(label).FontSize(fontSizePt).FontColor(theme.TextHex);
            });
    }

    /// <summary>The rectangle (canvas coords) a WPF <c>&lt;Image Stretch="Uniform"&gt;</c> with a
    /// uniform <paramref name="inset"/> margin gives content of intrinsic size
    /// <paramref name="contentW"/>×<paramref name="contentH"/> inside a node box: uniformly scaled
    /// (up or down — WPF Uniform upscales too) and centered on both axes.</summary>
    public static (double X, double Y, double Width, double Height) UniformFitRect(
        double nodeX, double nodeY, double nodeW, double nodeH, double inset, double contentW, double contentH)
    {
        double boxW = Math.Max(nodeW - 2 * inset, 1);
        double boxH = Math.Max(nodeH - 2 * inset, 1);
        if (contentW <= 0 || contentH <= 0) return (nodeX + inset, nodeY + inset, boxW, boxH);

        double fit = Math.Min(boxW / contentW, boxH / contentH);
        double drawW = contentW * fit, drawH = contentH * fit;
        return (nodeX + inset + (boxW - drawW) / 2,
                nodeY + inset + (boxH - drawH) / 2,
                drawW, drawH);
    }

    /// <summary>Parse an SVG's intrinsic size from its root element: numeric <c>width</c>/<c>height</c>
    /// attributes first (what <see cref="CodeToSvgRenderer"/> emits), <c>viewBox</c> as fallback.</summary>
    public static bool TryGetSvgSize(string svg, out double width, out double height)
    {
        width = height = 0;
        if (string.IsNullOrWhiteSpace(svg)) return false;
        try
        {
            var doc = System.Xml.Linq.XDocument.Parse(svg);
            var root = doc.Root;
            if (root is null || root.Name.LocalName != "svg") return false;

            static bool TryNum(string? raw, out double value)
            {
                value = 0;
                if (string.IsNullOrWhiteSpace(raw)) return false;
                var trimmed = raw.Trim();
                if (trimmed.EndsWith("px", StringComparison.OrdinalIgnoreCase))
                    trimmed = trimmed[..^2];
                return double.TryParse(trimmed, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out value) && value > 0;
            }

            if (TryNum(root.Attribute("width")?.Value, out width) &&
                TryNum(root.Attribute("height")?.Value, out height))
                return true;

            var viewBox = root.Attribute("viewBox")?.Value;
            if (!string.IsNullOrWhiteSpace(viewBox))
            {
                var parts = viewBox.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 4 &&
                    double.TryParse(parts[2], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out width) &&
                    double.TryParse(parts[3], System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out height) &&
                    width > 0 && height > 0)
                    return true;
            }
        }
        catch
        {
            // fall through — malformed SVG reports no intrinsic size
        }
        width = height = 0;
        return false;
    }

    /// <summary>Rough wrapped-text height (canvas px) for vertical centering — deliberately a
    /// cheap estimate (average Segoe UI advance ≈ 0.52 em, line height ≈ 1.35 em) rather than a
    /// WPF FormattedText measurement, so composition stays testable off the UI thread.</summary>
    public static double EstimateTextHeight(string text, double fontSize, double maxWidth)
    {
        if (string.IsNullOrEmpty(text)) return fontSize * 1.35;
        double charWidth = fontSize * 0.52;
        int charsPerLine = Math.Max(1, (int)(maxWidth / charWidth));
        int lines = text.Replace("\r\n", "\n").Split('\n')
            .Sum(line => Math.Max(1, (int)Math.Ceiling(line.Length / (double)charsPerLine)));
        return lines * fontSize * 1.35;
    }

    /// <summary>Default LaTeX rasterizer: WpfMath at 300 DPI (needs an STA thread), colored with
    /// the canvas text color so dark-theme exports keep their contrast against the page color.</summary>
    private static byte[]? RenderLatexPng(string formula, string foregroundHex)
    {
        try
        {
            var texFormula = WpfTeXFormulaParser.Instance.Parse(LatexPreprocessor.Normalize(formula));
            var foreground = new SolidColorBrush(
                CanvasThemeService.TryParseColor(foregroundHex, out var color) ? color : System.Windows.Media.Colors.Black);
            foreground.Freeze();

            // Scale 18 matches the FormulaControl in the canvas node template.
            var environment = WpfTeXEnvironment.Create(TexStyle.Display, 18.0, "Arial", foreground);
            var bitmap = texFormula.RenderToBitmap(environment, scale: 18.0, dpi: 300.0);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            return stream.ToArray();
        }
        catch
        {
            return null; // invalid formula → the caller falls back to selectable raw source text
        }
    }
}
