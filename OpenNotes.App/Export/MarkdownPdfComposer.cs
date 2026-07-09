using Markdig;
using Markdig.Wpf; // UseSupportedExtensions (matches the on-screen MarkdownViewer pipeline)
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace OpenNotes.Export;

/// <summary>Composes markdown as selectable rich PDF text (headings, bold/italic, lists, code,
/// quotes) via a Markdig AST walk emitting QuestPDF spans. Deliberately WPF-free so it can run
/// inside the worker-thread QuestPDF composition; fidelity target is the canvas MarkdownViewer,
/// not pixel-identity.</summary>
public static class MarkdownPdfComposer
{
    /// <summary>Heading size scale per level (level 4+ uses the last entry).</summary>
    private static readonly float[] HeadingScale = [1.6f, 1.4f, 1.2f, 1.05f];

    // Same extension set as the live MarkdownViewer and MarkdownImageRenderer, so this fallback
    // parses the identical AST (tables, task lists, autolinks). Extension node types the walker
    // below doesn't special-case degrade gracefully through the ContainerBlock-recurse and
    // LeafBlock/LeafInline literal-text catch-alls.
    private static readonly MarkdownPipeline Pipeline =
        new MarkdownPipelineBuilder().UseSupportedExtensions().Build();

    /// <summary>Compose <paramref name="markdown"/> into <paramref name="container"/> as rich
    /// text. <paramref name="fontSizePt"/> is the body size (already page-scaled);
    /// <paramref name="mutedHex"/> colors inline/fenced code.</summary>
    public static void Compose(IContainer container, string markdown, float fontSizePt,
                               string textHex, string mutedHex)
    {
        var document = Markdig.Markdown.Parse(markdown ?? string.Empty, Pipeline);
        container.Column(col => ComposeBlocks(col, document, fontSizePt, textHex, mutedHex));
    }

    private static void ComposeBlocks(ColumnDescriptor col, ContainerBlock blocks,
                                      float fontSizePt, string textHex, string mutedHex)
    {
        foreach (var block in blocks)
        {
            switch (block)
            {
                case HeadingBlock heading:
                    var scale = HeadingScale[Math.Clamp(heading.Level, 1, HeadingScale.Length) - 1];
                    col.Item().PaddingBottom(fontSizePt * 0.25f).Text(t =>
                    {
                        t.DefaultTextStyle(s => s.FontSize(fontSizePt * scale).SemiBold().FontColor(textHex));
                        ComposeInlines(t, heading.Inline, fontSizePt * scale, textHex, mutedHex, bold: true, italic: false);
                    });
                    break;

                case ParagraphBlock paragraph:
                    col.Item().PaddingBottom(fontSizePt * 0.2f).Text(t =>
                    {
                        t.DefaultTextStyle(s => s.FontSize(fontSizePt).FontColor(textHex));
                        ComposeInlines(t, paragraph.Inline, fontSizePt, textHex, mutedHex, bold: false, italic: false);
                    });
                    break;

                case ListBlock list:
                    var number = 1;
                    foreach (var item in list.OfType<ListItemBlock>())
                    {
                        var marker = list.IsOrdered ? $"{number++}." : "•";
                        col.Item().Row(row =>
                        {
                            row.ConstantItem(fontSizePt * 1.4f)
                               .Text(marker).FontSize(fontSizePt).FontColor(textHex);
                            row.RelativeItem().Column(inner =>
                                ComposeBlocks(inner, item, fontSizePt, textHex, mutedHex));
                        });
                    }
                    break;

                case CodeBlock code: // fenced and indented alike
                    col.Item().PaddingVertical(fontSizePt * 0.2f).Text(t =>
                    {
                        t.DefaultTextStyle(s => s
                            .FontFamily("Consolas", "Courier New")
                            .FontSize(fontSizePt * 0.92f)
                            .FontColor(mutedHex));
                        t.Span(code.Lines.ToString());
                    });
                    break;

                case QuoteBlock quote:
                    col.Item().PaddingLeft(fontSizePt).Column(inner =>
                        ComposeBlocks(inner, quote, fontSizePt, mutedHex, mutedHex));
                    break;

                case ThematicBreakBlock:
                    col.Item().PaddingVertical(fontSizePt * 0.3f).LineHorizontal(0.5f).LineColor(mutedHex);
                    break;

                case ContainerBlock container: // any other container: recurse
                    ComposeBlocks(col, container, fontSizePt, textHex, mutedHex);
                    break;

                case LeafBlock { Inline: not null } leaf: // any other leaf with inline content
                    col.Item().Text(t =>
                    {
                        t.DefaultTextStyle(s => s.FontSize(fontSizePt).FontColor(textHex));
                        ComposeInlines(t, leaf.Inline, fontSizePt, textHex, mutedHex, bold: false, italic: false);
                    });
                    break;
            }
        }
    }

    private static void ComposeInlines(TextDescriptor t, ContainerInline? inlines, float fontSizePt,
                                       string textHex, string mutedHex, bool bold, bool italic)
    {
        if (inlines is null) return;
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    Style(t.Span(literal.Content.ToString()));
                    break;

                case EmphasisInline emphasis:
                    var makesBold = emphasis.DelimiterCount >= 2;
                    ComposeInlines(t, emphasis, fontSizePt, textHex, mutedHex,
                        bold || makesBold, italic || !makesBold);
                    break;

                case CodeInline code:
                    Style(t.Span(code.Content).FontFamily("Consolas", "Courier New").FontColor(mutedHex));
                    break;

                case LineBreakInline:
                    t.Span("\n");
                    break;

                case LinkInline link when !link.IsImage:
                    // Offline app: render the link text as plain styled text, no hyperlink target.
                    ComposeInlines(t, link, fontSizePt, textHex, mutedHex, bold, italic);
                    break;

                case ContainerInline container: // html/autolink wrappers etc.: recurse
                    ComposeInlines(t, container, fontSizePt, textHex, mutedHex, bold, italic);
                    break;

                case LeafInline leaf: // anything else: fall back to its literal text
                    var text = leaf.ToString();
                    if (!string.IsNullOrEmpty(text)) Style(t.Span(text));
                    break;
            }

            continue;

            TextSpanDescriptor Style(TextSpanDescriptor span)
            {
                span.FontSize(fontSizePt).FontColor(textHex);
                if (bold) span.Bold();
                if (italic) span.Italic();
                return span;
            }
        }
    }
}
