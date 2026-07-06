using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using OpenNotes.Export;
using OpenNotes.ViewModels.Blocks;

namespace OpenNotes.Views.Blocks;

public partial class MermaidBlockView : UserControl
{
    private bool _webViewReady;
    private string? _pendingDefinition;

    public MermaidBlockView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        RenderButton.Click += (_, _) => RenderCurrent();
        Loaded += async (_, _) => await InitWebViewAsync();
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is MermaidBlockViewModel vm)
        {
            vm.PropertyChanged += (_, pe) =>
            {
                if (pe.PropertyName == nameof(vm.Definition))
                    RenderDefinition(vm.Definition);
            };
        }
    }

    private async Task InitWebViewAsync()
    {
        try
        {
            Serilog.Log.Debug("Mermaid preview: EnsureCoreWebView2Async starting");
            await MermaidWebView.EnsureCoreWebView2Async();
            Serilog.Log.Debug("Mermaid preview: CoreWebView2 ready ({Version})",
                MermaidWebView.CoreWebView2.Environment.BrowserVersionString);
            MermaidWebView.CoreWebView2.Settings.IsWebMessageEnabled = true;
            MermaidWebView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
            _webViewReady = true;

            if (_pendingDefinition is not null)
                RenderDefinition(_pendingDefinition);
            else if (DataContext is MermaidBlockViewModel vm)
                RenderDefinition(vm.Definition);
        }
        catch (Exception ex)
        {
            // WebView2 runtime not available – show fallback
            Serilog.Log.Warning(ex, "Mermaid preview WebView2 initialization failed.");
            if (DataContext is MermaidBlockViewModel vm)
                vm.ErrorMessage = "Diagram preview unavailable (WebView2 init failed).";
        }
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        var msg = e.TryGetWebMessageAsString();
        Serilog.Log.Debug("Mermaid preview message: {Message}", msg);
        if (msg?.StartsWith("error:") == true && DataContext is MermaidBlockViewModel vm)
            Dispatcher.Invoke(() => vm.ErrorMessage = msg[6..]);
    }

    private void RenderCurrent()
    {
        if (DataContext is MermaidBlockViewModel vm)
            RenderDefinition(vm.Definition);
    }

    private void RenderDefinition(string definition)
    {
        if (!_webViewReady)
        {
            _pendingDefinition = definition;
            return;
        }

        var html = MermaidHtmlBuilder.Build(MermaidHtmlBuilder.Escape(definition));
        MermaidWebView.NavigateToString(html);

        if (DataContext is MermaidBlockViewModel vm)
            vm.ErrorMessage = null;
    }
}
