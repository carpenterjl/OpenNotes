namespace OpenNotes.Interfaces;

/// <summary>Renders a Mermaid definition to a static PNG bitmap by screenshotting an off-screen
/// WebView2 (Chromium), so canvas snapshots are pixel-identical to the live WebView2 preview — an
/// SVG-text-then-SharpVectors conversion (the prior approach) uses a different rendering engine and
/// can never guarantee that.</summary>
public interface IMermaidSvgExporter
{
    Task<byte[]?> RenderToPngAsync(string definition, CancellationToken ct = default);

    /// <summary>Render with Mermaid <c>themeVariables</c> color overrides (variable name → hex,
    /// see <c>MermaidHtmlBuilder.BuildInitOptions</c>). Null falls back to the stock theme.</summary>
    Task<byte[]?> RenderToPngAsync(string definition, IReadOnlyDictionary<string, string>? themeVariables,
        CancellationToken ct = default);
}
