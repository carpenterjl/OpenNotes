using OpenNotes.Models;

namespace OpenNotes.Export;

/// <summary>One ink stroke reduced to plain data: page-space points extracted from the WPF
/// <c>StrokeCollection</c> (node-bound strokes already translated by their node's position),
/// plus the drawing attributes needed to redraw it as a vector path.</summary>
public sealed record CanvasPdfInkStroke(
    IReadOnlyList<(double X, double Y)> Points,
    string ColorHex,
    double Opacity,
    double Width,
    bool IsHighlighter);

/// <summary>Everything needed to compose one PDF page, prepared on the calling (UI) thread so
/// the QuestPDF composition itself is free of WPF and file I/O and can run on a worker thread.
/// Crop is the content bounding box (plus margin) in canvas coordinates — the PDF page shows
/// this window, not the full fixed canvas surface.</summary>
public sealed class CanvasPdfPageModel
{
    public required string Title { get; init; }
    public required double CropX { get; init; }
    public required double CropY { get; init; }
    public required double CropWidth { get; init; }
    public required double CropHeight { get; init; }
    public required IReadOnlyList<DiagramNode> Nodes { get; init; }
    public required IReadOnlyList<DiagramConnector> Connectors { get; init; }
    /// <summary>Raster content per node id: Image-node PNG bytes and 300-DPI LaTeX renders.</summary>
    public required IReadOnlyDictionary<Guid, byte[]> NodeImages { get; init; }
    /// <summary>Export-time re-rendered SVG snapshots per node id (code nodes re-colored with the
    /// export theme). Nodes absent here fall back to their persisted <c>SvgContent</c>.</summary>
    public IReadOnlyDictionary<Guid, string> NodeSvgs { get; init; } =
        new Dictionary<Guid, string>();
    /// <summary>Pixel-perfect markdown-note rasters per node id (the live WPF MarkdownViewer rendered
    /// to a transparent PNG on the UI thread). Nodes absent here fall back to selectable text.</summary>
    public IReadOnlyDictionary<Guid, byte[]> NodeMarkdownImages { get; init; } =
        new Dictionary<Guid, byte[]>();
    public required IReadOnlyList<CanvasPdfInkStroke> InkStrokes { get; init; }
}
