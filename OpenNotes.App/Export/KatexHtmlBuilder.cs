using System.Text.Json;
using System.Text.RegularExpressions;
using OpenNotes.Services;

namespace OpenNotes.Export;

/// <summary>Builds the self-contained HTML page that renders a LaTeX formula via the BUNDLED
/// KaTeX assets (<see cref="WebViewAssets.KatexJsUrl"/>/<see cref="WebViewAssets.KatexCssUrl"/> —
/// fully offline, no CDN; hosts must call <c>WebViewAssets.ConfigureVirtualHost</c> after WebView2
/// init). Mirrors <see cref="MermaidHtmlBuilder"/>'s shape: the page posts <c>done</c> or
/// <c>error:&lt;msg&gt;</c> back through the WebView2 host channel. The live block additionally
/// calls the page's global <c>window.renderLatex(source, displayMode)</c> for flicker-free
/// re-render: it renders into a DETACHED element and swaps it in only on success, so the last
/// good formula stays visible while the user types through an invalid intermediate state.</summary>
public static class KatexHtmlBuilder
{
    /// <summary>Build the full HTML document. <paramref name="formula"/> is the RAW normalized
    /// LaTeX (embedded as a JSON string literal — no manual escaping needed). <paramref name="colorHex"/>
    /// colors the formula via CSS <c>color</c> (KaTeX inherits currentColor, including <c>\boxed</c>
    /// borders and rules); invalid hex falls back to a neutral. <paramref name="forExport"/> renders
    /// on a transparent, padding-less body for bitmap capture; the live preview gets the editor
    /// surface background and centers the formula.</summary>
    public static string Build(string formula, bool displayMode, string colorHex,
        bool forExport = false, double fontSizePx = 20)
    {
        if (!IsHexColor(colorHex)) colorHex = "#E0E0E0";
        var bodyBg = forExport ? "transparent" : "#1E1E2E";
        var bodyPad = forExport ? "0" : "12px";
        var centering = forExport ? "" : "display: flex; align-items: center; justify-content: center; min-height: 100vh; box-sizing: border-box;";
        var sourceJson = JsonSerializer.Serialize(formula ?? string.Empty);
        var displayJson = displayMode ? "true" : "false";
        var fontSize = fontSizePx.ToString(System.Globalization.CultureInfo.InvariantCulture);

        return $$"""
            <!DOCTYPE html>
            <html>
            <head>
            <meta charset="utf-8">
            <link rel="stylesheet" href="{{WebViewAssets.KatexCssUrl}}">
            <style>
              html, body { margin: 0; }
              body { padding: {{bodyPad}}; background: {{bodyBg}}; {{centering}} }
              #out { display: inline-block; color: {{colorHex}}; font-size: {{fontSize}}px; }
            </style>
            </head>
            <body>
            <div id="out"></div>
            <script>
              window.addEventListener('error', function (e) {
                try { window.chrome.webview.postMessage('error:' + (e.message || 'script load failed')); } catch {}
              }, true);
              setTimeout(function () {
                if (typeof katex === 'undefined') {
                  try { window.chrome.webview.postMessage('error:katex did not load (bundled asset missing)'); } catch {}
                }
              }, 8000);
            </script>
            <script src="{{WebViewAssets.KatexJsUrl}}"></script>
            <script>
              // Render into a detached element; swap into #out only on success so the last good
              // render survives an invalid intermediate formula. Reports done/error every call.
              window.renderLatex = function (src, display) {
                try {
                  const probe = document.createElement('div');
                  katex.render(src, probe, { displayMode: display, throwOnError: true });
                  const out = document.getElementById('out');
                  out.innerHTML = probe.innerHTML;
                  try { window.chrome.webview.postMessage('done'); } catch {}
                  return true;
                } catch (e) {
                  try { window.chrome.webview.postMessage('error:' + e.message); } catch {}
                  return false;
                }
              };
              window.renderLatex({{sourceJson}}, {{displayJson}});
            </script>
            </body></html>
            """;
    }

    /// <summary>Convenience for the export path: normalize + build in one step.</summary>
    public static string BuildForExport(string rawFormula, string colorHex, double fontSizePx = 20)
    {
        var normalized = KatexInput.Normalize(rawFormula, out var displayMode);
        return Build(normalized, displayMode, colorHex, forExport: true, fontSizePx);
    }

    public static bool IsHexColor(string? s) =>
        s is not null && Regex.IsMatch(s, "^#(?:[0-9A-Fa-f]{3}|[0-9A-Fa-f]{6}|[0-9A-Fa-f]{8})$");
}
