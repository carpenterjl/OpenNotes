using OpenNotes.Interfaces;
using OpenNotes.Models;

namespace OpenNotes.UndoRedo.Commands;

public class TaskPropertyChangeCommand<T> : IUndoableCommand
{
    private readonly TaskItem _task;
    private readonly string _propertyName;
    private readonly T _oldValue;
    private readonly T _newValue;
    private readonly Action<TaskItem, T> _setter;
    private readonly Func<Task> _persistAsync;

    public string Description { get; }

    public TaskPropertyChangeCommand(
        TaskItem task,
        string propertyName,
        T oldValue,
        T newValue,
        Action<TaskItem, T> setter,
        Func<Task> persistAsync)
    {
        _task = task;
        _propertyName = propertyName;
        _oldValue = oldValue;
        _newValue = newValue;
        _setter = setter;
        _persistAsync = persistAsync;
        Description = $"Change {propertyName}";
    }

    public void Execute()
    {
        _setter(_task, _newValue);
        _ = _persistAsync();
    }

    public void Unexecute()
    {
        _setter(_task, _oldValue);
        _ = _persistAsync();
    }
}
