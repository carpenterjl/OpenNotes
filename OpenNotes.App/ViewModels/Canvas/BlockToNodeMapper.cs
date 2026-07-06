using System.Text;
using OpenNotes.Export;
using OpenNotes.Interfaces;
using OpenNotes.Models;
using OpenNotes.Models.Blocks;

namespace OpenNotes.ViewModels.Canvas;

/// <summary>Converts a task's <see cref="ContentBlock"/> into a canvas <see cref="DiagramNode"/> as a
/// one-time snapshot (no live link back to the original block). Heavy blocks (Mermaid, Code) are
/// captured as static images rather than embedding a live WebView2/AvalonEdit control: Mermaid is a
/// PNG screenshot of the actual WebView2 rendering (pixel-identical to the live preview), Code is a
/// self-contained SVG (see <see cref="CodeToSvgRenderer"/>, no rendering-engine to match).</summary>
public static class BlockToNodeMapper
{
    /// <param name="savePngAsync">Persists captured Mermaid PNG bytes and returns a file path for
    /// <see cref="DiagramNode.ImagePath"/> — supplied by the caller since only it has workspace
    /// context (this mapper is a static, workspace-agnostic utility).</param>
    /// <param name="mermaidThemeVariables">Optional Mermaid themeVariables color overrides (the
    /// document's custom canvas theme); null renders with the stock theme.</param>
    /// <param name="codePalette">Optional card colors for code-SVG snapshots (the document's
    /// effective canvas colors); null renders the stock light card.</param>
    public static async Task<DiagramNode> ToNodeAsync(
        ContentBlock block, double x, double y, Guid layerId,
        IMermaidSvgExporter mermaidExporter, Func<byte[], Task<string>> savePngAsync,
        IReadOnlyDictionary<string, string>? mermaidThemeVariables = null,
        CodeSvgPalette? codePalette = null, CancellationToken ct = default)
    {
        var node = new DiagramNode
        {
            LayerId = layerId, X = x, Y = y,
            SourceContentBlockId = block.Id, // link back so the node can be "updated in task"
        };

        switch (block)
        {
            case TextBlock t:
                node.Shape = NodeShape.Text;
                node.BlockKind = "text";
                node.Label = t.Content;
                node.AuthoredSource = t.Content;
                node.Width = 200; node.Height = 60;
                break;

            case MarkdownBlock m:
                node.Shape = NodeShape.StickyNote;
                node.BlockKind = "markdown";
                node.Label = m.Markdown;
                node.AuthoredSource = m.Markdown;
                node.Width = 240; node.Height = 140;
                break;

            case LatexBlock l:
                node.Shape = NodeShape.Latex;
                node.BlockKind = "latex";
                node.LatexContent = l.Formula;
                node.AuthoredSource = l.Formula;
                node.Width = 200; node.Height = 80;
                break;

            case ImageBlock img:
                node.Shape = NodeShape.Image;
                node.BlockKind = "image";
                node.ImagePath = img.FilePath;
                node.Label = img.Caption ?? string.Empty;
                node.Width = 240; node.Height = 180;
                break;

            case ChecklistBlock c:
                node.Shape = NodeShape.Checklist;
                node.BlockKind = "checklist";
                node.Label = FlattenChecklist(c);
                node.AuthoredSource = node.Label;
                node.Width = 220; node.Height = Math.Max(60, 24 + c.Items.Count * 18);
                break;

            case MermaidBlock mer:
                node.BlockKind = "mermaid";
                node.AuthoredSource = mer.Definition;
                var png = await mermaidExporter.RenderToPngAsync(mer.Definition, mermaidThemeVariables, ct);
                if (png is null)
                {
                    node.Shape = NodeShape.Text;
                    node.Label = "Mermaid (render failed)";
                }
                else
                {
                    node.Shape = NodeShape.Image;
                    node.ImagePath = await savePngAsync(png);
                }
                node.Width = 280; node.Height = 220;
                break;

            case CodeBlock code:
                node.Shape = NodeShape.Svg;
                node.BlockKind = "code";
                node.AuthoredSource = code.Code;
                node.AuthoredLanguage = code.Language;
                node.SvgContent = CodeToSvgRenderer.Render(code.Code, code.Language, code.ShowLineNumbers, codePalette);
                node.Width = 320; node.Height = 200;
                break;

            default:
                node.Shape = NodeShape.Text;
                node.Label = block.GetType().Name;
                node.Width = 200; node.Height = 60;
                break;
        }

        return node;
    }

    /// <summary>Flatten a checklist to one <c>☑/☐ text</c> line per item (display + round-trip source).</summary>
    public static string FlattenChecklist(ChecklistBlock c)
    {
        var sb = new StringBuilder();
        foreach (var item in c.Items)
            sb.AppendLine($"{(item.IsCompleted ? "☑" : "☐")} {item.Text}");
        return sb.ToString().TrimEnd();
    }
}
