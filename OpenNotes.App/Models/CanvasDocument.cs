namespace OpenNotes.Models;

/// <summary>Manifest stored as <c>document.json</c> inside a <c>.taskcanvas</c> archive:
/// document metadata, ordered page list, and custom theme colors. Page payloads live in
/// separate <c>pages/{id}.json</c> entries so a manifest read never parses node data.</summary>
public class CanvasDocumentManifest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    /// <summary>Archive schema version; bump when the entry layout changes.</summary>
    public int FormatVersion { get; set; } = 1;
    public string Title { get; set; } = "New Canvas";
    /// <summary>Owning task, or null for a standalone workspace canvas.</summary>
    public Guid? OwnerTaskId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
    /// <summary>Ordered pages. The order of this list IS the page order.</summary>
    public List<CanvasPageInfo> Pages { get; set; } = [];
    /// <summary>Custom theme color overrides, keyed by resource name (e.g. "AccentBrush" → "#AACCEE").</summary>
    public Dictionary<string, string> ThemeColors { get; set; } = [];
}

public class CanvasPageInfo
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "Page 1";
}

/// <summary>One page of a canvas document: the node/connector payload plus the page-level
/// floating-ink blob (WPF Ink Serialized Format) and a cached PNG thumbnail for grid overviews.
/// Node-bound ink lives on each <see cref="DiagramNode.InkData"/> instead.</summary>
public class CanvasPage
{
    public CanvasPage(CanvasPageInfo info, DiagramModel diagram)
    {
        Info = info;
        Diagram = diagram;
    }

    public CanvasPageInfo Info { get; }
    public DiagramModel Diagram { get; }
    /// <summary>Page-global floating ink strokes, ISF bytes (<c>pages/{id}.isf</c>). Null = no ink.</summary>
    public byte[]? FloatingInk { get; set; }
    /// <summary>Cached page thumbnail PNG (<c>pages/{id}.thumb.png</c>), captured on save/page switch.</summary>
    public byte[]? Thumbnail { get; set; }
}

/// <summary>In-memory representation of a <c>.taskcanvas</c> document archive.</summary>
public class CanvasDocument
{
    public CanvasDocumentManifest Manifest { get; set; } = new();
    public List<CanvasPage> Pages { get; } = [];

    /// <summary>Append a new empty page (default-titled "Page N") and register it in the manifest.</summary>
    public CanvasPage AddPage(string? title = null)
    {
        var info = new CanvasPageInfo { Title = title ?? $"Page {Pages.Count + 1}" };
        var page = new CanvasPage(info, new DiagramModel { Title = info.Title });
        Manifest.Pages.Add(info);
        Pages.Add(page);
        return page;
    }

    /// <summary>Remove a page and its manifest entry. The last remaining page cannot be removed.</summary>
    public bool RemovePage(Guid pageId)
    {
        if (Pages.Count <= 1) return false;
        var page = Pages.FirstOrDefault(p => p.Info.Id == pageId);
        if (page is null) return false;
        Pages.Remove(page);
        Manifest.Pages.Remove(page.Info);
        return true;
    }
}
