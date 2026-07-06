using System.Text.RegularExpressions;

namespace OpenNotes.Export;

/// <summary>Builds the self-contained HTML page that renders a Mermaid definition via the
/// mermaid ESM module. Shared by the live <c>MermaidBlockView</c> preview and the off-screen
/// <c>MermaidSvgExporter</c> so both use identical escaping and initialization.</summary>
public static class MermaidHtmlBuilder
{
    /// <summary>Escape a raw Mermaid definition for safe embedding inside the HTML template's
    /// <c>&lt;div class="mermaid"&gt;</c> literal.</summary>
    public static string Escape(string definition) =>
        definition.Replace("\\", "\\\\").Replace("`", "\\`").Replace("$", "\\$");

    /// <summary>Build the full HTML document for an already-escaped definition. After
    /// <c>mermaid.run()</c> resolves the page posts <c>done</c> (or <c>error:&lt;msg&gt;</c>) back
    /// through the WebView2 host channel so callers have a deterministic completion signal.</summary>
    /// <param name="forExport">When true, render for bitmap capture: a transparent body (so the
    /// captured PNG composites cleanly onto the canvas node's own background) instead of the live
    /// preview's opaque editor background, and zero body padding (the exporter sizes the host to the
    /// SVG's own bounds plus a tiny margin, so page padding would just add dead space to the capture).
    /// Theme and label rendering are otherwise identical to the live preview — canvas snapshots are a
    /// WebView2 screenshot (see <c>MermaidSvgExporter</c>), not an SVG-to-SharpVectors conversion, so
    /// there's no separate rendering-engine-compat mode to keep in sync anymore. Supersampling for the
    /// capture is handled by the exporter via <c>CoreWebView2Controller.ZoomFactor</c> — the documented
    /// content-scaling API — not a CSS <c>zoom</c> baked into this HTML.</param>
    /// <param name="themeVariables">Mermaid <c>themeVariables</c> (variable name → hex color) applied
    /// via the customizable <c>base</c> theme. Null/empty keeps the stock <c>dark</c> theme used by the
    /// live block preview. Values are validated as hex colors before being embedded in the script.</param>
    public static string Build(string escapedDefinition, bool forExport = false,
        IReadOnlyDictionary<string, string>? themeVariables = null)
    {
        var bodyBg = forExport ? "transparent" : "#1E1E2E";
        var bodyPad = forExport ? "0" : "12px";
        // Live preview scales the SVG with a transform, which doesn't affect layout size —
        // clip instead of showing scrollbars mid-drag. Export must never clip.
        var bodyOverflow = forExport ? "visible" : "hidden";
        // Live preview only: scale the diagram (up or down) to fit the WebView2 box, and re-fit on
        // every viewport resize so the diagram tracks the block's resize grip. Done in JS post-render
        // (not CSS) because a CSS percentage/viewport-relative height chain collapsed to zero when
        // WebView2's viewport bounds weren't finalized yet, blanking the whole page. Scaling sets the
        // SVG's real width/height (it has a viewBox, so content scales with the box) rather than a
        // CSS transform: a transform changes only the pixels, not the layout box, so the oversized
        // layout box kept overflowing the centered flex body and the edges clipped. The natural SVG
        // size is measured once with max-width cleared, before any resizing, so later re-fits are
        // computed from the true size. Skipped for export — MermaidSvgExporter sizes its off-screen
        // host to the SVG's natural bounds and supersamples via ZoomFactor, so it must never be
        // pre-scaled here.
        var fitScript = forExport ? "" : """

                const svg = document.querySelector('svg');
                if (svg) {
                  svg.style.maxWidth = 'none';
                  const rect = svg.getBoundingClientRect();
                  const naturalW = Math.max(1, rect.width);
                  const naturalH = Math.max(1, rect.height);
                  if (!svg.getAttribute('viewBox'))
                    svg.setAttribute('viewBox', '0 0 ' + naturalW + ' ' + naturalH);
                  const pad = 12;
                  const fit = () => {
                    const availW = Math.max(1, window.innerWidth - pad * 2);
                    const availH = Math.max(1, window.innerHeight - pad * 2);
                    const scale = Math.min(availW / naturalW, availH / naturalH);
                    svg.style.width = (naturalW * scale) + 'px';
                    svg.style.height = (naturalH * scale) + 'px';
                  };
                  fit();
                  window.addEventListener('resize', fit);
                  try { window.chrome.webview.postMessage('debug:svg ' + naturalW + 'x' + naturalH + ' win ' + window.innerWidth + 'x' + window.innerHeight); } catch {}
                }
            """;

        return $$"""
            <!DOCTYPE html>
            <html>
            <head>
            <meta charset="utf-8">
            <style>
              html, body { margin: 0; }
              body { padding: {{bodyPad}}; background: {{bodyBg}}; display: flex; justify-content: center; {{(forExport ? "" : "align-items: center; ")}}overflow: {{bodyOverflow}}; }
              .mermaid { font-family: 'Segoe UI', sans-serif; }
              svg { max-width: 100%; height: auto; }
            </style>
            </head>
            <body>
            <div class="mermaid">{{escapedDefinition}}</div>
            <script>
              // A failed ESM import (offline, CDN unreachable, DNS…) kills the module script
              // before its try/catch exists, leaving a silently blank page. Surface it: capture
              // page-level errors, and if no SVG materialized after 8s report a load failure.
              window.addEventListener('error', function (e) {
                try { window.chrome.webview.postMessage('error:' + (e.message || 'script load failed')); } catch {}
              }, true);
              setTimeout(function () {
                if (!document.querySelector('svg')) {
                  try { window.chrome.webview.postMessage('error:mermaid did not render (script blocked or network unavailable)'); } catch {}
                }
              }, 8000);
            </script>
            <script type="module">
              import mermaid from 'https://cdn.jsdelivr.net/npm/mermaid@11/dist/mermaid.esm.min.mjs';
              mermaid.initialize({{BuildInitOptions(themeVariables)}});
              try {
                await mermaid.run();{{fitScript}}
                window.chrome.webview.postMessage('done');
              } catch (e) {
                window.chrome.webview.postMessage('error:' + e.message);
              }
            </script>
            </body></html>
            """;
    }

    /// <summary>The <c>mermaid.initialize</c> options literal. With theme variables: the customizable
    /// <c>base</c> theme plus the given variable→hex pairs; otherwise the stock <c>dark</c> theme.
    /// Only identifier-shaped names and hex-color values are emitted, so untrusted or malformed
    /// entries can never break out of the script literal.</summary>
    public static string BuildInitOptions(IReadOnlyDictionary<string, string>? themeVariables)
    {
        var pairs = themeVariables?
            .Where(kv => IsIdentifier(kv.Key) && IsHexColor(kv.Value))
            .Select(kv => $"{kv.Key}: '{kv.Value}'")
            .ToList();

        return pairs is { Count: > 0 }
            ? $"{{ startOnLoad: false, theme: 'base', themeVariables: {{ {string.Join(", ", pairs)} }} }}"
            : "{ startOnLoad: false, theme: 'dark' }";
    }

    private static bool IsIdentifier(string s) => Regex.IsMatch(s, "^[A-Za-z][A-Za-z0-9]*$");

    private static bool IsHexColor(string s) => Regex.IsMatch(s, "^#(?:[0-9A-Fa-f]{3}|[0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$");
}
