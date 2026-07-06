using System.IO;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Markdig;
using Markdig.Wpf; // UseSupportedExtensions (matches the on-screen MarkdownViewer pipeline)

namespace OpenNotes.Export;

/// <summary>Rasterizes a canvas markdown note to a PNG so the PDF is pixel-identical to the canvas
/// rather than an approximate text reflow. Builds the same <see cref="FlowDocument"/> the on-screen
/// <c>MarkdownViewer</c> uses (via <c>Markdig.Wpf</c> + the globally-merged themed Markdig styles) and
/// renders it through the document paginator — a control rendered off the visual tree lays out empty,
/// but a paginated <see cref="DocumentPage"/> is fully arranged. Must run on the UI thread (WPF layout
/// + <see cref="RenderTargetBitmap"/>); the exporter builds its page models there. Background is
/// transparent so the PNG overlays the vector sticky-note rectangle from the SVG chrome layer.</summary>
public static class MarkdownImageRenderer
{
    // Match the on-screen MarkdownViewer's default pipeline so formatting is identical.
    private static readonly MarkdownPipeline Pipeline =
        new MarkdownPipelineBuilder().UseSupportedExtensions().Build();

    /// <summary>Render <paramref name="markdown"/> into a <paramref name="widthPx"/>×
    /// <paramref name="heightPx"/> transparent PNG (clipped like the on-screen note), text in
    /// <paramref name="foreground"/>, at <paramref name="dpiScale"/>× for print crispness. Returns
    /// null on failure so the caller can fall back to the text composer.</summary>
    public static byte[]? Render(string markdown, double widthPx, double heightPx,
                                 double fontSizePx, Color foreground, double dpiScale = 2.0)
    {
        try
        {
            if (widthPx < 1 || heightPx < 1 || string.IsNullOrWhiteSpace(markdown)) return null;

            var fg = new SolidColorBrush(foreground);
            fg.Freeze();

            var doc = Markdig.Wpf.Markdown.ToFlowDocument(markdown, Pipeline, null);
            doc.ColumnWidth = double.PositiveInfinity; // single column, no column gap
            doc.PageWidth = widthPx;
            doc.PageHeight = heightPx;
            doc.PagePadding = new Thickness(0);
            doc.FontSize = fontSizePx;
            doc.Background = Brushes.Transparent;
            TextElement.SetForeground(doc, fg); // inherited by body text + headings

            var paginator = ((IDocumentPaginatorSource)doc).DocumentPaginator;
            paginator.PageSize = new Size(widthPx, heightPx);
            paginator.ComputePageCount();
            if (paginator.PageCount == 0) return null;

            // Page 0 only — the note clips overflow on screen (ClipToBounds), so a single page window
            // of exactly the node size matches what the user sees.
            using var page = paginator.GetPage(0);

            var rtb = new RenderTargetBitmap(
                (int)Math.Ceiling(widthPx * dpiScale), (int)Math.Ceiling(heightPx * dpiScale),
                96 * dpiScale, 96 * dpiScale, PixelFormats.Pbgra32);
            rtb.Render(page.Visual); // RTB's DPI scales the DIP-sized visual to the pixel dimensions

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            return stream.ToArray();
        }
        catch
        {
            return null;
        }
    }
}
