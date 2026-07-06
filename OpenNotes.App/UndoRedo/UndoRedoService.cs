using Microsoft.Extensions.Logging;
using OpenNotes.Interfaces;

namespace OpenNotes.UndoRedo;

public class UndoRedoService : IUndoRedoService
{
    private readonly ILogger<UndoRedoService> _logger;
    private readonly Stack<IUndoableCommand> _undoStack = new();
    private readonly Stack<IUndoableCommand> _redoStack = new();
    private int _maxHistory = 200;

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;
    public string? NextUndoDescription => CanUndo ? _undoStack.Peek().Description : null;
    public string? NextRedoDescription => CanRedo ? _redoStack.Peek().Description : null;
    public int HistoryDepth => _undoStack.Count;

    public event EventHandler? StateChanged;

    public UndoRedoService(ILogger<UndoRedoService> logger)
    {
        _logger = logger;
    }

    public void Push(IUndoableCommand command)
    {
        command.Execute();
        _undoStack.Push(command);
        _redoStack.Clear();

        // Trim history if exceeded
        if (_undoStack.Count > _maxHistory)
        {
            var overflow = _undoStack.ToArray().Skip(_maxHistory).ToArray();
            var trimmed = new Stack<IUndoableCommand>(_undoStack.ToArray().Take(_maxHistory).Reverse());
            _undoStack.Clear();
            foreach (var cmd in trimmed)
                _undoStack.Push(cmd);
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
        _logger.LogDebug("Pushed command: {Description}", command.Description);
    }

    public void Undo()
    {
        if (!CanUndo) return;
        var command = _undoStack.Pop();
        command.Unexecute();
        _redoStack.Push(command);
        StateChanged?.Invoke(this, EventArgs.Empty);
        _logger.LogDebug("Undid: {Description}", command.Description);
    }

    public void Redo()
    {
        if (!CanRedo) return;
        var command = _redoStack.Pop();
        command.Execute();
        _undoStack.Push(command);
        StateChanged?.Invoke(this, EventArgs.Empty);
        _logger.LogDebug("Redid: {Description}", command.Description);
    }

    public void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
