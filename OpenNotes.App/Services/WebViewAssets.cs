using System.IO;
using Microsoft.Web.WebView2.Core;

namespace OpenNotes.Services;

/// <summary>Single source of truth for the bundled offline web assets (Mermaid UMD + KaTeX
/// js/css/fonts under <c>Resources\web</c>, see <c>Resources\web\VERSIONS.md</c>). Pages built by
/// <c>MermaidHtmlBuilder</c>/<c>KatexHtmlBuilder</c> reference them via the
/// <c>https://opennotes.assets/…</c> virtual host, which <see cref="ConfigureVirtualHost"/> maps
/// onto the on-disk folder. Call it right after every <c>EnsureCoreWebView2Async</c> — pages loaded
/// through <c>NavigateToString</c> have a null origin, so the mapping must use
/// <see cref="CoreWebView2HostResourceAccessKind.Allow"/> (DenyCors would block both the script
/// loads and the KaTeX font fetches).</summary>
public static class WebViewAssets
{
    public const string HostName = "opennotes.assets";

    public const string MermaidJsUrl = $"https://{HostName}/mermaid/mermaid.min.js";
    public const string KatexJsUrl = $"https://{HostName}/katex/katex.min.js";
    public const string KatexCssUrl = $"https://{HostName}/katex/katex.min.css";

    /// <summary>The deployed assets folder next to the executable.</summary>
    public static string RootPath => Path.Combine(AppContext.BaseDirectory, "Resources", "web");

    /// <summary>Map the virtual host onto the assets folder for this CoreWebView2 instance.
    /// Idempotent (re-mapping the same host name simply replaces the mapping).</summary>
    public static void ConfigureVirtualHost(CoreWebView2 core) =>
        core.SetVirtualHostNameToFolderMapping(HostName, RootPath, CoreWebView2HostResourceAccessKind.Allow);
}
