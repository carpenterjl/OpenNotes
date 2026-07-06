using OpenNotes.Models;

namespace OpenNotes.Interfaces;

/// <summary>The effective (override-or-theme-default) colors a canvas document renders with,
/// captured as hex strings so PDF generation needs no live WPF resource lookups and can run
/// off the UI thread.</summary>
public record CanvasPdfTheme(
    string PageBackgroundHex,
    string TextHex,
    string AccentHex,
    string SurfaceHex,
    string StickyFillHex = "#FFF9C4",
    string StickyStrokeHex = "#E0D060",
    string StickyTextHex = "#33301E")
{
    public static CanvasPdfTheme Default { get; } = new("#FFFFFF", "#1E1E1E", "#7B9CDF", "#F5F5F5");
}

/// <summary>Exports a canvas document as a high-fidelity PDF: one PDF page per canvas page,
/// text/markdown/checklist labels as selectable PDF text, LaTeX/Mermaid as high-DPI images,
/// and shapes/connectors/ink strokes as native vector paths.</summary>
public interface ICanvasPdfExporter
{
    Task ExportAsync(CanvasDocument document, CanvasPdfTheme theme, string outputPath, CancellationToken ct = default);
}
