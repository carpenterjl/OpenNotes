using System.Text.Json.Serialization;

namespace OpenNotes.Models.Blocks;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(TextBlock), "text")]
[JsonDerivedType(typeof(MarkdownBlock), "markdown")]
[JsonDerivedType(typeof(LatexBlock), "latex")]
[JsonDerivedType(typeof(MermaidBlock), "mermaid")]
[JsonDerivedType(typeof(CodeBlock), "code")]
[JsonDerivedType(typeof(ImageBlock), "image")]
[JsonDerivedType(typeof(ChecklistBlock), "checklist")]
[JsonDerivedType(typeof(TableBlock), "table")]
[JsonDerivedType(typeof(CalloutBlock), "callout")]
[JsonDerivedType(typeof(SvgBlock), "svg")]
[JsonDerivedType(typeof(EmbeddedDiagramBlock), "embedded_diagram")]
public abstract class ContentBlock
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int Order { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;

    /// <summary>User-set editor height in the task editor. Null = auto-size to content.</summary>
    public double? Height { get; set; }
}

public class TextBlock : ContentBlock
{
    public string Content { get; set; } = string.Empty;
}

public class MarkdownBlock : ContentBlock
{
    public string Markdown { get; set; } = string.Empty;
}

public class LatexBlock : ContentBlock
{
    public string Formula { get; set; } = string.Empty;
    public bool DisplayMode { get; set; } = true;
}

public class MermaidBlock : ContentBlock
{
    public string Definition { get; set; } = string.Empty;
}

public class CodeBlock : ContentBlock
{
    public string Code { get; set; } = string.Empty;
    public string Language { get; set; } = "plaintext";
    public bool ShowLineNumbers { get; set; } = true;
}

public class ImageBlock : ContentBlock
{
    public string FilePath { get; set; } = string.Empty;
    public string? Caption { get; set; }
    public double? WidthPercent { get; set; }
}

public class ChecklistBlock : ContentBlock
{
    public List<ChecklistItem> Items { get; set; } = [];
}

public class TableBlock : ContentBlock
{
    public List<string> Headers { get; set; } = [];
    public List<List<string>> Rows { get; set; } = [];
}

public enum CalloutType { Info, Warning, Error, Success, Note, Tip }

public class CalloutBlock : ContentBlock
{
    public CalloutType CalloutType { get; set; } = CalloutType.Info;
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}

public class SvgBlock : ContentBlock
{
    public string SvgContent { get; set; } = string.Empty;
}

public class EmbeddedDiagramBlock : ContentBlock
{
    public Guid DiagramId { get; set; }
    public string DiagramTitle { get; set; } = string.Empty;
}
