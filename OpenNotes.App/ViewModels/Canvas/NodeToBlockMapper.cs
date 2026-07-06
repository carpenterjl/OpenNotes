using OpenNotes.Models;
using OpenNotes.Models.Blocks;

namespace OpenNotes.ViewModels.Canvas;

/// <summary>Converts a block-backed canvas node back into a task <see cref="ContentBlock"/> for the
/// "Add / Update in task" push-back. Keyed on <see cref="CanvasNodeViewModel.BlockKind"/> (falling back
/// to the node shape). When the node already links to a block, its Id is reused so the push updates that
/// block in place rather than appending a duplicate.</summary>
public static class NodeToBlockMapper
{
    /// <summary>True if this node can be turned into a content block (has a resolvable kind).</summary>
    public static bool CanConvert(CanvasNodeViewModel node) => ResolveKind(node) is not null;

    public static ContentBlock ToBlock(CanvasNodeViewModel node)
    {
        var kind = ResolveKind(node)
            ?? throw new InvalidOperationException($"Node shape {node.Shape} has no content-block mapping.");

        ContentBlock block = kind switch
        {
            "text" => new TextBlock { Content = node.AuthoredSource ?? node.Label },
            "markdown" => new MarkdownBlock { Markdown = node.AuthoredSource ?? node.Label },
            "latex" => new LatexBlock { Formula = node.AuthoredSource ?? node.LatexContent ?? string.Empty },
            "image" => new ImageBlock { FilePath = node.ImagePath ?? string.Empty, Caption = string.IsNullOrWhiteSpace(node.Label) ? null : node.Label },
            "checklist" => new ChecklistBlock { Items = ParseChecklist(node.AuthoredSource ?? node.Label) },
            "code" => new CodeBlock { Code = node.AuthoredSource ?? string.Empty, Language = node.AuthoredLanguage ?? "plaintext" },
            "mermaid" => new MermaidBlock { Definition = node.AuthoredSource ?? string.Empty },
            _ => new TextBlock { Content = node.AuthoredSource ?? node.Label },
        };

        // Preserve identity so a re-push updates the same block instead of creating a new one.
        if (node.SourceContentBlockId is Guid id)
            block.Id = id;

        return block;
    }

    /// <summary>Map a node to a content-block discriminator, using the explicit BlockKind when present
    /// and otherwise inferring from the visual shape (for shapes that map cleanly to a block).</summary>
    private static string? ResolveKind(CanvasNodeViewModel node)
    {
        if (!string.IsNullOrEmpty(node.BlockKind)) return node.BlockKind;
        return node.Shape switch
        {
            NodeShape.Text => "text",
            NodeShape.StickyNote => "markdown",
            NodeShape.Latex => "latex",
            NodeShape.Image => "image",
            NodeShape.Checklist => "checklist",
            _ => null, // bare shapes / SVG without a kind can't be reversed
        };
    }

    private static List<ChecklistItem> ParseChecklist(string text)
    {
        var items = new List<ChecklistItem>();
        if (string.IsNullOrWhiteSpace(text)) return items;

        int order = 0;
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.TrimStart();
            if (line.Length == 0) continue;

            bool done = line.StartsWith('☑') || line.StartsWith("[x]", StringComparison.OrdinalIgnoreCase);
            var t = line.TrimStart('☑', '☐').Trim();
            if (t.StartsWith("[x]", StringComparison.OrdinalIgnoreCase) || t.StartsWith("[ ]"))
                t = t[3..].Trim();

            items.Add(new ChecklistItem { Text = t, IsCompleted = done, Order = order++ });
        }
        return items;
    }
}
