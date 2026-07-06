using OpenNotes.Models;

namespace OpenNotes.Interfaces;

public interface ITaskService
{
    event EventHandler<TaskItem>? TaskCreated;
    event EventHandler<TaskItem>? TaskUpdated;
    event EventHandler<Guid>? TaskDeleted;

    Task<IReadOnlyList<TaskItem>> GetTasksAsync(Guid workspaceId, CancellationToken ct = default);
    Task<TaskItem?> GetTaskAsync(Guid workspaceId, Guid taskId, CancellationToken ct = default);
    Task<TaskItem> CreateTaskAsync(Guid workspaceId, string title, Guid? parentId = null, CancellationToken ct = default);
    Task UpdateTaskAsync(Guid workspaceId, TaskItem task, CancellationToken ct = default);
    Task DeleteTaskAsync(Guid workspaceId, Guid taskId, CancellationToken ct = default);
    Task<TaskItem> CloneTaskAsync(Guid workspaceId, Guid taskId, CancellationToken ct = default);
    Task MoveTaskAsync(Guid fromWorkspaceId, Guid toWorkspaceId, Guid taskId, CancellationToken ct = default);
    Task BatchUpdateStatusAsync(Guid workspaceId, IEnumerable<Guid> taskIds, TaskStatus status, CancellationToken ct = default);
    Task BatchDeleteAsync(Guid workspaceId, IEnumerable<Guid> taskIds, CancellationToken ct = default);
    Task<IReadOnlyList<TaskItem>> GetSubtasksAsync(Guid workspaceId, Guid parentId, CancellationToken ct = default);
    Task<IReadOnlyList<TaskItem>> GetDueTasksAsync(Guid workspaceId, DateTime from, DateTime to, CancellationToken ct = default);
    Task<IReadOnlyList<TaskItem>> GetOverdueTasksAsync(Guid workspaceId, CancellationToken ct = default);
}
