using Microsoft.Extensions.Logging;
using OpenNotes.Interfaces;
using OpenNotes.Models;
using OpenNotes.Persistence;

namespace OpenNotes.Services;

public class TaskService : ITaskService
{
    private readonly TaskRepository _repository;
    private readonly IAutosaveService _autosave;
    private readonly ILogger<TaskService> _logger;

    public event EventHandler<TaskItem>? TaskCreated;
    public event EventHandler<TaskItem>? TaskUpdated;
    public event EventHandler<Guid>? TaskDeleted;

    public TaskService(
        TaskRepository repository,
        IAutosaveService autosave,
        ILogger<TaskService> logger)
    {
        _repository = repository;
        _autosave = autosave;
        _logger = logger;
    }

    public async Task<IReadOnlyList<TaskItem>> GetTasksAsync(Guid workspaceId, CancellationToken ct = default)
    {
        var tasks = await _repository.GetAllAsync(workspaceId, ct);
        return tasks.OrderByDescending(t => t.CreatedAt).ToList().AsReadOnly();
    }

    public async Task<TaskItem?> GetTaskAsync(Guid workspaceId, Guid taskId, CancellationToken ct = default)
        => await _repository.GetAsync(workspaceId, taskId, ct);

    public async Task<TaskItem> CreateTaskAsync(
        Guid workspaceId, string title, Guid? parentId = null, CancellationToken ct = default)
    {
        var task = new TaskItem
        {
            Title = title,
            ParentId = parentId,
            Status = TaskStatus.NotStarted
        };

        await _repository.SaveAsync(workspaceId, task, ct);
        _autosave.MarkDirty();
        TaskCreated?.Invoke(this, task);
        _logger.LogDebug("Created task '{Title}' in workspace {WorkspaceId}", title, workspaceId);
        return task;
    }

    public async Task UpdateTaskAsync(Guid workspaceId, TaskItem task, CancellationToken ct = default)
    {
        await _repository.SaveAsync(workspaceId, task, ct);
        _autosave.MarkDirty();
        TaskUpdated?.Invoke(this, task);
    }

    public async Task DeleteTaskAsync(Guid workspaceId, Guid taskId, CancellationToken ct = default)
    {
        await _repository.DeleteAsync(workspaceId, taskId, ct);
        _autosave.MarkDirty();
        TaskDeleted?.Invoke(this, taskId);
    }

    public async Task<TaskItem> CloneTaskAsync(Guid workspaceId, Guid taskId, CancellationToken ct = default)
    {
        var source = await _repository.GetAsync(workspaceId, taskId, ct)
            ?? throw new InvalidOperationException($"Task {taskId} not found");

        var clone = new TaskItem
        {
            Title = $"{source.Title} (Copy)",
            Description = source.Description,
            Status = TaskStatus.NotStarted,
            Priority = source.Priority,
            Tags = [.. source.Tags],
            Categories = [.. source.Categories],
            ContentBlocks = [.. source.ContentBlocks],
            Checklist = source.Checklist.Select(c => new ChecklistItem
            {
                Text = c.Text,
                Order = c.Order
            }).ToList()
        };

        await _repository.SaveAsync(workspaceId, clone, ct);
        _autosave.MarkDirty();
        TaskCreated?.Invoke(this, clone);
        return clone;
    }

    public async Task MoveTaskAsync(
        Guid fromWorkspaceId, Guid toWorkspaceId, Guid taskId, CancellationToken ct = default)
    {
        var task = await _repository.GetAsync(fromWorkspaceId, taskId, ct)
            ?? throw new InvalidOperationException($"Task {taskId} not found");

        await _repository.SaveAsync(toWorkspaceId, task, ct);
        await _repository.DeleteAsync(fromWorkspaceId, taskId, ct);
        _autosave.MarkDirty();
    }

    public async Task BatchUpdateStatusAsync(
        Guid workspaceId, IEnumerable<Guid> taskIds, TaskStatus status, CancellationToken ct = default)
    {
        foreach (var taskId in taskIds)
        {
            ct.ThrowIfCancellationRequested();
            var task = await _repository.GetAsync(workspaceId, taskId, ct);
            if (task is null) continue;
            task.Status = status;
            if (status == TaskStatus.Completed)
                task.CompletedAt = DateTime.UtcNow;
            await _repository.SaveAsync(workspaceId, task, ct);
            TaskUpdated?.Invoke(this, task);
        }
        _autosave.MarkDirty();
    }

    public async Task BatchDeleteAsync(
        Guid workspaceId, IEnumerable<Guid> taskIds, CancellationToken ct = default)
    {
        foreach (var taskId in taskIds)
        {
            ct.ThrowIfCancellationRequested();
            await _repository.DeleteAsync(workspaceId, taskId, ct);
            TaskDeleted?.Invoke(this, taskId);
        }
        _autosave.MarkDirty();
    }

    public async Task<IReadOnlyList<TaskItem>> GetSubtasksAsync(
        Guid workspaceId, Guid parentId, CancellationToken ct = default)
    {
        var all = await _repository.GetAllAsync(workspaceId, ct);
        return all.Where(t => t.ParentId == parentId)
                  .OrderBy(t => t.CreatedAt)
                  .ToList()
                  .AsReadOnly();
    }

    public async Task<IReadOnlyList<TaskItem>> GetDueTasksAsync(
        Guid workspaceId, DateTime from, DateTime to, CancellationToken ct = default)
    {
        var all = await _repository.GetAllAsync(workspaceId, ct);
        return all.Where(t => t.DueDate.HasValue && t.DueDate.Value >= from && t.DueDate.Value <= to)
                  .OrderBy(t => t.DueDate)
                  .ToList()
                  .AsReadOnly();
    }

    public async Task<IReadOnlyList<TaskItem>> GetOverdueTasksAsync(
        Guid workspaceId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var all = await _repository.GetAllAsync(workspaceId, ct);
        return all.Where(t =>
                t.DueDate.HasValue &&
                t.DueDate.Value < now &&
                t.Status != TaskStatus.Completed &&
                t.Status != TaskStatus.Cancelled)
            .OrderBy(t => t.DueDate)
            .ToList()
            .AsReadOnly();
    }
}
