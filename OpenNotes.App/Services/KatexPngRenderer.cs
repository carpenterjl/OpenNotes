using Microsoft.Extensions.Logging;
using OpenNotes.Export;
using OpenNotes.Interfaces;

namespace OpenNotes.Services;

/// <summary>KaTeX-based LaTeX → PNG renderer: builds the offline KaTeX export page and delegates
/// the off-screen render/measure/supersample/capture to the shared <see cref="IHtmlPngExporter"/>.
/// The fixed inline-block <c>#out</c> wrapper is the measured element, so one selector covers both
/// inline and display formulas.</summary>
public sealed class KatexPngRenderer : ILatexPngRenderer
{
    private readonly IHtmlPngExporter _exporter;
    private readonly ILogger<KatexPngRenderer> _logger;

    public KatexPngRenderer(IHtmlPngExporter exporter, ILogger<KatexPngRenderer> logger)
    {
        _exporter = exporter;
        _logger = logger;
    }

    public async Task<byte[]?> RenderToPngAsync(string formula, string colorHex, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(formula)) return null;

        var html = KatexHtmlBuilder.BuildForExport(formula, colorHex);
        var png = await _exporter.RenderToPngAsync(html, "#out", ct);
        if (png is null)
            _logger.LogWarning("KaTeX PNG render produced no image for formula starting {Start}…",
                formula.Length > 24 ? formula[..24] : formula);
        return png;
    }
}
