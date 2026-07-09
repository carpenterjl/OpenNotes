using System.IO;
using System.Text.Json;
using System.Windows;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using OpenNotes.Interfaces;

namespace OpenNotes.Services;

/// <summary>Captures arbitrary HTML content as a PNG bitmap by rendering it in a hidden,
/// off-screen WebView2 (there is no truly headless mode without a composition controller, so a
/// small off-screen <see cref="Window"/> is used) and screenshotting the actual Chromium-rendered
/// output — pixel-identical to a live WebView2 preview of the same page. The window/WebView2 are
/// created lazily and reused across renders. Generalized from the original Mermaid-only exporter;
/// <see cref="MermaidSvgExporter"/> and <see cref="KatexPngRenderer"/> are thin adapters over
/// this pipeline.</summary>
public sealed class HtmlPngExporter : IHtmlPngExporter, IDisposable
{
    /// <summary>Supersampling factor applied via <c>CoreWebView2Controller.ZoomFactor</c> before
    /// capture so the PNG stays sharp when the canvas node is scaled up (4x, per product decision).
    /// This is the documented content-scaling API — preferred over a CSS <c>zoom</c> baked into
    /// the HTML.</summary>
    private const double OversampleFactor = 4.0;

    /// <summary>Extra device pixels added around the measured content so anti-aliased edges aren't
    /// clipped by an off-by-one in the size computation.</summary>
    private const int EdgeMarginPx = 8;

    private readonly ILogger<HtmlPngExporter> _logger;

    private Window? _host;
    private WebView2? _webView;
    private bool _coreReady;
    private TaskCompletionSource<bool>? _pending; // signalled by the page's done/error message
    private string? _lastError;
    private readonly SemaphoreSlim _gate = new(1, 1); // one render at a time (single shared WebView2)

    public HtmlPngExporter(ILogger<HtmlPngExporter> logger) => _logger = logger;

    public async Task<byte[]?> RenderToPngAsync(string html, string measureSelector, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(html)) return null;
        if (Application.Current is null) return null; // no UI (unit tests) — nothing to render into

        await _gate.WaitAsync(ct);
        try
        {
            if (!await EnsureWebViewAsync())
                return null;

            // --- Phase 1: render + measure at 1x -------------------------------------------------
            // Measure the content's native (un-zoomed) CSS size while ZoomFactor is 1.0. DOM
            // geometry for fixed-size content is computed independent of the host viewport, so a
            // small host doesn't shrink the measurement — and measuring with no zoom applied
            // sidesteps any Chromium-version ambiguity over whether getBoundingClientRect() folds
            // in the zoom factor. (The WPF WebView2 control surfaces the controller's ZoomFactor
            // directly.)
            _webView!.ZoomFactor = 1.0;

            _lastError = null;
            _pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            _webView.CoreWebView2.NavigateToString(html);

            // Wait for the page's completion signal, a timeout, or cancellation.
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(15));
            using (timeoutCts.Token.Register(() => _pending?.TrySetCanceled()))
            {
                try { await _pending.Task; }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning("HTML PNG export timed out or was cancelled.");
                    return null;
                }
            }

            if (_lastError is not null)
            {
                _logger.LogWarning("Render error during HTML PNG export: {Error}", _lastError);
                return null;
            }

            var rectRaw = await _webView.CoreWebView2.ExecuteScriptAsync(
                "(function(){var s=document.querySelector(" + JsonSerializer.Serialize(measureSelector) + ");" +
                "if(!s)return null;var r=s.getBoundingClientRect();return [r.width, r.height];})()");
            var dims = JsonSerializer.Deserialize<double[]>(rectRaw);
            if (dims is not { Length: 2 } || dims[0] <= 0 || dims[1] <= 0)
            {
                _logger.LogWarning("HTML PNG export could not measure the rendered content size ({Selector}).",
                    measureSelector);
                return null;
            }

            // --- Phase 2: size the host to fit the 4x content, once, then verify + capture --------
            // Target the exact supersampled content size (+ a small anti-alias margin) so no
            // post-crop is ever needed. Resize the host ONCE, then poll until the native controller
            // bounds have actually caught up before capturing — CapturePreviewAsync grabs whatever
            // bounds exist at call time, and the WPF resize → CoreWebView2Controller.Bounds →
            // Chromium repaint chain is three independent async stages, so a blind delay could
            // screenshot the stale (smaller) view.
            var targetW = (int)Math.Ceiling(dims[0] * OversampleFactor) + EdgeMarginPx;
            var targetH = (int)Math.Ceiling(dims[1] * OversampleFactor) + EdgeMarginPx;

            _host!.Width = targetW;
            _host.Height = targetH;
            _webView.Width = targetW;
            _webView.Height = targetH;
            _webView.ZoomFactor = OversampleFactor;

            await WaitForBoundsAsync(targetW, targetH, ct);

            using var stream = new MemoryStream();
            await _webView.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
            return stream.Length > 0 ? stream.ToArray() : null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export HTML content to PNG.");
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Poll until the WebView2 control's laid-out size reaches the requested size (or a short
    /// timeout elapses), so the subsequent capture sees the resized viewport rather than a stale one.
    /// Verifies the actual propagated bounds instead of guessing with a fixed delay. <c>ActualWidth</c>/
    /// <c>ActualHeight</c> update only after a layout pass, which is what drives the underlying
    /// <c>CoreWebView2Controller.Bounds</c> the capture reads from.</summary>
    private async Task WaitForBoundsAsync(int targetW, int targetH, CancellationToken ct)
    {
        const int PollIntervalMs = 25;
        const int TimeoutMs = 500;

        for (var waited = 0; waited < TimeoutMs; waited += PollIntervalMs)
        {
            if (_webView!.ActualWidth >= targetW && _webView.ActualHeight >= targetH)
                return;
            await Task.Delay(PollIntervalMs, ct);
        }

        _logger.LogDebug("HTML PNG export: control did not reach {W}x{H} within {Timeout}ms; capturing anyway.",
            targetW, targetH, TimeoutMs);
    }

    private async Task<bool> EnsureWebViewAsync()
    {
        if (_coreReady && _webView is not null) return true;

        _host ??= new Window
        {
            Width = 1,
            Height = 1,
            Left = -10000,
            Top = -10000,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
            ShowActivated = false,
            Focusable = false,
        };

        if (_webView is null)
        {
            _webView = new WebView2
            {
                DefaultBackgroundColor = System.Drawing.Color.Transparent,
                // Own user-data folder → own browser process. The live Mermaid/LaTeX previews use
                // WebView2CompositionControls on the DEFAULT environment; a browser process
                // hosting composition controllers rejects this control's *windowed* controller
                // (CreateCoreWebView2ControllerAsync fails), so the exporter must not share it.
                CreationProperties = new CoreWebView2CreationProperties
                {
                    UserDataFolder = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "OpenNotes", "cache", "webview2-export"),
                },
            };
            _host.Content = _webView;
            _host.Show(); // required so the WebView2 gets an HWND; it's off-screen and never focused
        }

        try
        {
            await _webView.EnsureCoreWebView2Async();
            _webView.CoreWebView2.Settings.IsWebMessageEnabled = true;
            _webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
            // Bundled offline assets (mermaid.min.js, katex.*) resolve through the virtual host.
            WebViewAssets.ConfigureVirtualHost(_webView.CoreWebView2);
            _coreReady = true;
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "WebView2 runtime unavailable — HTML PNG export disabled.");
            return false;
        }
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        var msg = e.TryGetWebMessageAsString();
        if (msg is null) return;
        if (msg.StartsWith("debug:", StringComparison.Ordinal)) return; // telemetry only, not completion
        if (msg.StartsWith("error:", StringComparison.Ordinal))
            _lastError = msg[6..];
        _pending?.TrySetResult(true);
    }

    public void Dispose()
    {
        if (_webView is not null)
            _webView.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
        _webView?.Dispose();
        _host?.Close();
        _gate.Dispose();
    }
}
