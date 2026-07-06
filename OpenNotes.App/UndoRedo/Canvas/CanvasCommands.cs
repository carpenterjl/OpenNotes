using OpenNotes.Interfaces;
using OpenNotes.ViewModels;
using OpenNotes.ViewModels.Canvas;

namespace OpenNotes.UndoRedo.Canvas;

/// <summary>Add a node to the canvas. (The undo service calls Execute() on Push, which performs
/// the actual insertion.)</summary>
public sealed class AddNodeCommand(CanvasEditorViewModel canvas, CanvasNodeViewModel node) : IUndoableCommand
{
    public string Description => "Add shape";
    public void Execute() => canvas.AddNodeInternal(node);
    public void Unexecute() => canvas.RemoveNodeInternal(node);
}

/// <summary>Delete a node together with every connector attached to it.</summary>
public sealed class DeleteNodeCommand(
    CanvasEditorViewModel canvas,
    CanvasNodeViewModel node,
    IReadOnlyList<CanvasConnectorViewModel> attachedConnectors) : IUndoableCommand
{
    public string Description => "Delete shape";

    public void Execute()
    {
        foreach (var c in attachedConnectors) canvas.RemoveConnectorInternal(c);
        canvas.RemoveNodeInternal(node);
    }

    public void Unexecute()
    {
        canvas.AddNodeInternal(node);
        foreach (var c in attachedConnectors) canvas.AddConnectorInternal(c);
    }
}

/// <summary>Move a node from one position to another.</summary>
public sealed class MoveNodeCommand(
    CanvasNodeViewModel node, double oldX, double oldY, double newX, double newY) : IUndoableCommand
{
    public string Description => "Move shape";
    public void Execute() { node.X = newX; node.Y = newY; }
    public void Unexecute() { node.X = oldX; node.Y = oldY; }
}

/// <summary>Add a connector between two nodes.</summary>
public sealed class AddConnectorCommand(CanvasEditorViewModel canvas, CanvasConnectorViewModel connector) : IUndoableCommand
{
    public string Description => "Add connector";
    public void Execute() => canvas.AddConnectorInternal(connector);
    public void Unexecute() => canvas.RemoveConnectorInternal(connector);
}

/// <summary>Delete a connector.</summary>
public sealed class DeleteConnectorCommand(CanvasEditorViewModel canvas, CanvasConnectorViewModel connector) : IUndoableCommand
{
    public string Description => "Delete connector";
    public void Execute() => canvas.RemoveConnectorInternal(connector);
    public void Unexecute() => canvas.AddConnectorInternal(connector);
}

/// <summary>Show or hide a node's border chrome (context-menu toggle). The VM property
/// write-throughs to the model, so persistence and dirty tracking follow automatically.</summary>
public sealed class SetNodeBorderCommand(CanvasNodeViewModel node, bool show) : IUndoableCommand
{
    public string Description => show ? "Show border" : "Hide border";
    public void Execute() => node.ShowBorder = show;
    public void Unexecute() => node.ShowBorder = !show;
}

/// <summary>Change a node's z-order (collection order IS draw order: the nodes ItemsControl
/// renders in list order on a Canvas panel, and the persisted DiagramModel list matches).</summary>
public sealed class ReorderNodeCommand(
    CanvasEditorViewModel canvas, CanvasNodeViewModel node, int fromIndex, int toIndex) : IUndoableCommand
{
    public string Description => "Reorder shape";
    public void Execute() => canvas.MoveNodeToIndexInternal(node, toIndex);
    public void Unexecute() => canvas.MoveNodeToIndexInternal(node, fromIndex);
}

/// <summary>Resize a node from one X/Y/Width/Height to another (top/left-anchored resizes also move
/// the node, so this stores full geometry, not just size, mirroring <see cref="MoveNodeCommand"/>).</summary>
public sealed class ResizeNodeCommand(
    CanvasNodeViewModel node,
    double oldX, double oldY, double oldWidth, double oldHeight,
    double newX, double newY, double newWidth, double newHeight) : IUndoableCommand
{
    public string Description => "Resize shape";
    public void Execute() { node.X = newX; node.Y = newY; node.Width = newWidth; node.Height = newHeight; }
    public void Unexecute() { node.X = oldX; node.Y = oldY; node.Width = oldWidth; node.Height = oldHeight; }
}
