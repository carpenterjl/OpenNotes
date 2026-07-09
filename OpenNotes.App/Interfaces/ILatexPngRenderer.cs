namespace OpenNotes.Interfaces;

/// <summary>Renders a LaTeX formula to a transparent PNG via the KaTeX/WebView2 pipeline —
/// full LaTeX support (bold, \boxed, \mathbb, Unicode) that the WpfMath raster path lacks.
/// Used for canvas LaTeX node snapshots and reused by the PDF export.</summary>
public interface ILatexPngRenderer
{
    /// <param name="formula">Raw (un-normalized) LaTeX source, delimiters allowed.</param>
    /// <param name="colorHex">Formula color (the document's effective canvas text color).</param>
    /// <returns>PNG bytes (4x supersampled, transparent background), or null when rendering
    /// failed or WebView2 is unavailable.</returns>
    Task<byte[]?> RenderToPngAsync(string formula, string colorHex, CancellationToken ct = default);
}
