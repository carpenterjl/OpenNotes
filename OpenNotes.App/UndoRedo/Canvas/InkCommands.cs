using System.Windows.Ink;
using OpenNotes.Interfaces;
using OpenNotes.ViewModels.Canvas;

namespace OpenNotes.UndoRedo.Canvas;

/// <summary>Add a user-drawn stroke to the page's floating ink. The InkCanvas has already added the
/// stroke to the collection by the time StrokeCollected fires, so Execute (called immediately on
/// Push) must be idempotent.</summary>
public sealed class AddFloatingStrokeCommand(StrokeCollection strokes, Stroke stroke) : IUndoableCommand
{
    public string Description => "Draw ink";
    public void Execute() { if (!strokes.Contains(stroke)) strokes.Add(stroke); }
    public void Unexecute() { if (strokes.Contains(stroke)) strokes.Remove(stroke); }
}

/// <summary>Erase a floating-ink stroke (the eraser's own removal is cancelled and routed through
/// this command so the erase is undoable).</summary>
public sealed class EraseFloatingStrokeCommand(StrokeCollection strokes, Stroke stroke) : IUndoableCommand
{
    public string Description => "Erase ink";
    public void Execute() { if (strokes.Contains(stroke)) strokes.Remove(stroke); }
    public void Unexecute() { if (!strokes.Contains(stroke)) strokes.Add(stroke); }
}

/// <summary>Add a stroke (already translated to node-local coordinates) to a node's bound ink.
/// Undo removes the stroke entirely — it does not go back to the floating layer.</summary>
public sealed class AddNodeInkStrokeCommand(CanvasNodeViewModel node, Stroke stroke) : IUndoableCommand
{
    public string Description => "Draw ink on shape";
    public void Execute() { if (!node.InkStrokes.Contains(stroke)) node.InkStrokes.Add(stroke); }
    public void Unexecute() { if (node.InkStrokes.Contains(stroke)) node.InkStrokes.Remove(stroke); }
}

/// <summary>Erase a single node-bound ink stroke (the ⌫ eraser tool hit-testing node ink — the
/// InkCanvas can't reach it, so the view hit-tests manually and routes removals through here).</summary>
public sealed class EraseNodeInkStrokeCommand(CanvasNodeViewModel node, Stroke stroke) : IUndoableCommand
{
    public string Description => "Erase ink on shape";
    public void Execute() { if (node.InkStrokes.Contains(stroke)) node.InkStrokes.Remove(stroke); }
    public void Unexecute() { if (!node.InkStrokes.Contains(stroke)) node.InkStrokes.Add(stroke); }
}

/// <summary>Remove all of a node's bound ink (context-menu "Clear ink").</summary>
public sealed class ClearNodeInkCommand : IUndoableCommand
{
    private readonly CanvasNodeViewModel _node;
    private readonly List<Stroke> _removed;

    public ClearNodeInkCommand(CanvasNodeViewModel node)
    {
        _node = node;
        _removed = [.. node.InkStrokes];
    }

    public string Description => "Clear shape ink";
    public void Execute() => _node.InkStrokes.Clear();

    public void Unexecute()
    {
        foreach (var stroke in _removed)
            if (!_node.InkStrokes.Contains(stroke)) _node.InkStrokes.Add(stroke);
    }
}
