namespace OpenNotes.Models;

// NOTE: append new values only — System.Text.Json serializes enums as ints, so inserting
// mid-list would renumber and corrupt existing persisted diagrams. `Text` = a bare text label
// (rendered with no shape chrome).
public enum NodeShape { Rectangle, RoundedRectangle, Ellipse, Diamond, Parallelogram, Cylinder, Cloud, StickyNote, Container, Text, Latex, Image, Checklist, Svg, TaskLink }
public enum ConnectorStyle { Solid, Dashed, Dotted }
public enum ArrowType { None, Arrow, OpenArrow, Diamond, Circle }

public class DiagramModel
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "New Diagram";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
    public List<DiagramNode> Nodes { get; set; } = [];
    public List<DiagramConnector> Connectors { get; set; } = [];
    public double ViewportX { get; set; }
    public double ViewportY { get; set; }
    public double ZoomLevel { get; set; } = 1.0;
    public bool ShowGrid { get; set; } = true;
    public bool SnapToGrid { get; set; } = true;
    public double GridSize { get; set; } = 20.0;
    public bool ShowRulers { get; set; } = true;
    public List<DiagramLayer> Layers { get; set; } = [new DiagramLayer { Name = "Default" }];
    /// <summary>Canvas page size in pixels. Defaults preserve the previously-hardcoded 3000x2000
    /// surface, so diagrams persisted before this field existed deserialize unchanged.</summary>
    public double CanvasWidth { get; set; } = 3000;
    public double CanvasHeight { get; set; } = 2000;
}

public class DiagramNode
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid LayerId { get; set; }
    public NodeShape Shape { get; set; } = NodeShape.Rectangle;
    public string Label { get; set; } = string.Empty;
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; } = 120;
    public double Height { get; set; } = 60;
    public string FillColor { get; set; } = "#FFFFFF";
    public string StrokeColor { get; set; } = "#333333";
    public string TextColor { get; set; } = "#000000";
    public double StrokeThickness { get; set; } = 1.5;
    /// <summary>Whether the node's border/outline chrome is drawn (context-menu toggle). Defaults
    /// true — documents saved before the property existed load unchanged.</summary>
    public bool ShowBorder { get; set; } = true;
    public double FontSize { get; set; } = 12;
    public bool IsLocked { get; set; }
    public Guid? LinkedTaskId { get; set; }
    public string? ImagePath { get; set; }
    public string? LatexContent { get; set; }
    /// <summary>Captured static SVG markup for snapshot nodes (Mermaid diagrams, rendered code).</summary>
    public string? SvgContent { get; set; }
    /// <summary>Id of the task <c>ContentBlock</c> this node is linked to (snapshotted from, or committed
    /// to). Null = not block-backed. Drives update-in-place vs. append when pushing back to the task.</summary>
    public Guid? SourceContentBlockId { get; set; }
    /// <summary>The <c>ContentBlock</c> discriminator for a block-backed node ("text", "markdown", "code",
    /// "mermaid", "latex", "image", "checklist"). Disambiguates e.g. an Svg node as Code vs Mermaid.</summary>
    public string? BlockKind { get; set; }
    /// <summary>Canonical editable source for a block-backed node (Code=raw code, Mermaid=definition;
    /// mirrors Label/LatexContent for text-like kinds). Feeds re-render and reverse block mapping.</summary>
    public string? AuthoredSource { get; set; }
    /// <summary>Language for a code-backed node.</summary>
    public string? AuthoredLanguage { get; set; }
    public List<Guid> ChildNodeIds { get; set; } = [];
    public Guid? ParentNodeId { get; set; }
    /// <summary>Node-bound ink strokes (WPF Ink Serialized Format bytes, base64 in JSON). Strokes drawn
    /// fully inside this node's bounds are re-parented here so they move/persist with the node; page-level
    /// floating ink lives in the document archive's <c>pages/{id}.isf</c> entry instead.</summary>
    public byte[]? InkData { get; set; }
}

public class DiagramConnector
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SourceNodeId { get; set; }
    public Guid TargetNodeId { get; set; }
    public string Label { get; set; } = string.Empty;
    public ConnectorStyle Style { get; set; } = ConnectorStyle.Solid;
    public ArrowType SourceArrow { get; set; } = ArrowType.None;
    public ArrowType TargetArrow { get; set; } = ArrowType.Arrow;
    public string Color { get; set; } = "#555555";
    public double Thickness { get; set; } = 1.5;
    public List<DiagramPoint> Waypoints { get; set; } = [];
}

public class DiagramPoint
{
    public double X { get; set; }
    public double Y { get; set; }
}

public class DiagramLayer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public bool IsVisible { get; set; } = true;
    public bool IsLocked { get; set; }
    public int Order { get; set; }
}
