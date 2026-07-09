using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;
using OpenNotes.Export;
using OpenNotes.Services;
using OpenNotes.ViewModels.Blocks;

namespace OpenNotes.Views.Blocks;

/// <summary>Live LaTeX block: renders through KaTeX in a WebView2CompositionControl (full LaTeX
/// support — bold, \boxed, \mathbb, Unicode — which WpfMath could not do). Mirrors
/// <see cref="MermaidBlockView"/>'s init/postMessage pattern; formula edits go through the page's
/// <c>window.renderLatex</c> (no page reload — the last good render stays visible while the user
/// types through an invalid intermediate state, replacing the old VM-side RenderedFormula
/// mechanism). Theme changes are tracked via a DynamicResource-backed dependency property on
/// <c>TextPrimaryBrush</c> — when the theme swaps the brush, the page is rebuilt with the new
/// formula color (no service reference needed in a template-created view).</summary>
public partial class LatexBlockView : UserControl
{
    /// <summary>Tracks the theme's TextPrimaryBrush via SetResourceReference so theme switches
    /// (including Custom-theme color edits) re-color the rendered formula.</summary>
    private static readonly DependencyProperty ThemeTextBrushProperty = DependencyProperty.Register(
        nameof(ThemeTextBrush), typeof(Brush), typeof(LatexBlockView),
        new PropertyMetadata(null, static (d, _) => ((LatexBlockView)d).OnThemeBrushChanged()));

    private Brush? ThemeTextBrush => (Brush?)GetValue(ThemeTextBrushProperty);

    private bool _webViewReady;
    private LatexBlockViewModel? _vm;

    public LatexBlockView()
    {
        InitializeComponent();
        SetResourceReference(ThemeTextBrushProperty, "TextPrimaryBrush");
        DataContextChanged += OnDataContextChanged;
        Loaded += async (_, _) => await InitWebViewAsync();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is LatexBlockViewModel old)
            old.PropertyChanged -= OnVmPropertyChanged;
        _vm = e.NewValue as LatexBlockViewModel;
        if (_vm is not null)
            _vm.PropertyChanged += OnVmPropertyChanged;
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LatexBlockViewModel.Formula) or nameof(LatexBlockViewModel.DisplayMode))
            _ = RenderCurrentAsync();
    }

    private async Task InitWebViewAsync()
    {
        if (_webViewReady) { NavigateFreshPage(); return; }
        try
        {
            await LatexWebView.EnsureCoreWebView2Async();
            // Bundled offline katex assets resolve through the opennotes.assets virtual host.
            WebViewAssets.ConfigureVirtualHost(LatexWebView.CoreWebView2);
            LatexWebView.CoreWebView2.Settings.IsWebMessageEnabled = true;
            LatexWebView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
            _webViewReady = true;

            NavigateFreshPage();
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "LaTeX preview WebView2 initialization failed.");
            if (_vm is not null)
                _vm.ErrorMessage = "Formula preview unavailable (WebView2 init failed).";
        }
    }

    private void OnThemeBrushChanged()
    {
        if (_webViewReady)
            NavigateFreshPage();
    }

    /// <summary>Full page (re)build — initial load and theme changes (the formula color is baked
    /// into the page CSS). Ordinary formula edits use <see cref="RenderCurrentAsync"/> instead.</summary>
    private void NavigateFreshPage()
    {
        if (!_webViewReady || _vm is null) return;
        var normalized = KatexInput.Normalize(_vm.Formula, out var displayMode);
        var html = KatexHtmlBuilder.Build(normalized, displayMode, CurrentTextColorHex());
        LatexWebView.CoreWebView2.NavigateToString(html);
    }

    private async Task RenderCurrentAsync()
    {
        if (!_webViewReady || _vm is null) return;
        var normalized = KatexInput.Normalize(_vm.Formula, out var displayMode);
        if (string.IsNullOrEmpty(normalized))
        {
            _vm.ErrorMessage = null;
            await LatexWebView.CoreWebView2.ExecuteScriptAsync(
                "document.getElementById('out').innerHTML = '';");
            return;
        }
        await LatexWebView.CoreWebView2.ExecuteScriptAsync(
            $"window.renderLatex({JsonSerializer.Serialize(normalized)}, {(displayMode ? "true" : "false")});");
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        var msg = e.TryGetWebMessageAsString();
        if (msg is null || _vm is null) return;
        var vm = _vm;
        Dispatcher.Invoke(() =>
        {
            if (msg.StartsWith("error:", StringComparison.Ordinal))
                vm.ErrorMessage = msg[6..];
            else if (msg == "done")
                vm.ErrorMessage = null;
        });
    }

    /// <summary>The current theme's primary text color as hex (KaTeX colors via CSS).</summary>
    private string CurrentTextColorHex() =>
        ThemeTextBrush is SolidColorBrush b
            ? $"#{b.Color.R:X2}{b.Color.G:X2}{b.Color.B:X2}"
            : "#E0E0E0";
}
