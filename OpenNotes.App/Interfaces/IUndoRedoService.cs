namespace OpenNotes.Interfaces;

public interface IUndoableCommand
{
    string Description { get; }
    void Execute();
    void Unexecute();
}

public interface IUndoRedoService
{
    bool CanUndo { get; }
    bool CanRedo { get; }
    string? NextUndoDescription { get; }
    string? NextRedoDescription { get; }
    int HistoryDepth { get; }

    event EventHandler? StateChanged;

    void Push(IUndoableCommand command);
    void Undo();
    void Redo();
    void Clear();
}
