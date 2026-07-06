using Microsoft.Extensions.Logging;
using OpenNotes.Interfaces;
using OpenNotes.Models;

namespace OpenNotes.Persistence;

public class TaskRepository
{
    private readonly IPersistenceService _persistence;
    private readonly WorkspaceRepository _workspaceRepo;
    private readonly ILogger<TaskRepository> _logger;

    public TaskRepository(
        IPersistenceService persistence,
        WorkspaceRepository workspaceRepo,
        ILogger<TaskRepository> logger)
    {
        _persistence = persistence;
        _workspaceRepo = workspaceRepo;
        _logger = logger;
    }

    private string GetTaskPath(Guid workspaceId, Guid taskId)
        => Path.Combine(_workspaceRepo.GetTasksFolder(workspaceId), $"{taskId}.json");

    public async Task<List<TaskItem>> GetAllAsync(Guid workspaceId, CancellationToken ct = default)
    {
        var tasksFolder = _workspaceRepo.GetTasksFolder(workspaceId);
        var files = await _persistence.EnumerateFilesAsync(tasksFolder, "*.json");
        var tasks = new List<TaskItem>();

        foreach (var file in files)
        {
            ct.ThrowIfCancellationRequested();
            var task = await _persistence.ReadAsync<TaskItem>(file, ct);
            if (task is not null)
                tasks.Add(task);
        }

        return tasks;
    }

    public async Task<TaskItem?> GetAsync(Guid workspaceId, Guid taskId, CancellationToken ct = default)
        => await _persistence.ReadAsync<TaskItem>(GetTaskPath(workspaceId, taskId), ct);

    public async Task SaveAsync(Guid workspaceId, TaskItem task, CancellationToken ct = default)
    {
        task.ModifiedAt = DateTime.UtcNow;
        await _persistence.WriteAsync(GetTaskPath(workspaceId, task.Id), task, ct);
    }

    public async Task DeleteAsync(Guid workspaceId, Guid taskId, CancellationToken ct = default)
        => await _persistence.DeleteAsync(GetTaskPath(workspaceId, taskId), ct);

    public async Task<bool> ExistsAsync(Guid workspaceId, Guid taskId)
        => await _persistence.ExistsAsync(GetTaskPath(workspaceId, taskId));
}
