using Microsoft.Extensions.Logging;
using OpenNotes.Export;
using OpenNotes.Interfaces;

namespace OpenNotes.Services;

/// <summary>Captures a Mermaid definition as a PNG bitmap — a real Chromium screenshot of the
/// same page the live preview renders, so canvas snapshots are pixel-identical to it. Thin
/// adapter: builds the export HTML via <see cref="MermaidHtmlBuilder"/> and delegates the whole
/// off-screen render/measure/supersample/capture pipeline to <see cref="IHtmlPngExporter"/>
/// (shared with the KaTeX renderer).</summary>
public sealed class MermaidSvgExporter : IMermaidSvgExporter
{
    private readonly IHtmlPngExporter _exporter;
    private readonly ILogger<MermaidSvgExporter> _logger;

    public MermaidSvgExporter(IHtmlPngExporter exporter, ILogger<MermaidSvgExporter> logger)
    {
        _exporter = exporter;
        _logger = logger;
    }

    public Task<byte[]?> RenderToPngAsync(string definition, CancellationToken ct = default) =>
        RenderToPngAsync(definition, themeVariables: null, ct);

    public async Task<byte[]?> RenderToPngAsync(string definition,
        IReadOnlyDictionary<string, string>? themeVariables, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(definition)) return null;

        var html = MermaidHtmlBuilder.Build(MermaidHtmlBuilder.Escape(definition), forExport: true, themeVariables);
        var png = await _exporter.RenderToPngAsync(html, "svg", ct);
        if (png is null)
            _logger.LogWarning("Mermaid PNG export produced no image.");
        return png;
    }
}
