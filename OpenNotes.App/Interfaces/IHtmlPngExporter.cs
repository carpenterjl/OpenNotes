namespace OpenNotes.Interfaces;

/// <summary>Renders a self-contained HTML page off-screen in WebView2 and captures the result as
/// a supersampled PNG. The page must post <c>done</c> (or <c>error:&lt;msg&gt;</c>) through
/// <c>window.chrome.webview.postMessage</c> when rendering completes — the exporter waits for that
/// signal, measures the element selected by <paramref name="measureSelector"/>, sizes the host to
/// exactly fit it at 4x, and screenshots. Content-agnostic: Mermaid diagrams and KaTeX formulas
/// share this pipeline.</summary>
public interface IHtmlPngExporter
{
    /// <param name="html">Complete HTML document (typically via <c>NavigateToString</c>-safe builders).</param>
    /// <param name="measureSelector">CSS selector for the element whose bounding rect defines the
    /// capture size (e.g. <c>"svg"</c> for Mermaid, <c>"#out"</c> for KaTeX).</param>
    /// <returns>PNG bytes, or null when rendering failed or WebView2 is unavailable.</returns>
    Task<byte[]?> RenderToPngAsync(string html, string measureSelector, CancellationToken ct = default);
}
