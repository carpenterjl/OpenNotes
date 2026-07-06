using Microsoft.Extensions.Logging;
using OpenNotes.Interfaces;
using OpenNotes.Models;
using OpenNotes.Persistence;

namespace OpenNotes.Services;

public class WorkspaceService : IWorkspaceService
{
    private readonly WorkspaceRepository _repository;
    private readonly IPersistenceService _persistence;
    private readonly ILogger<WorkspaceService> _logger;

    private readonly List<WorkspaceMetadata> _workspaces = [];
    private WorkspaceMetadata? _activeWorkspace;

    public WorkspaceMetadata? ActiveWorkspace => _activeWorkspace;
    public IReadOnlyList<WorkspaceMetadata> AllWorkspaces => _workspaces.AsReadOnly();

    public event EventHandler<WorkspaceMetadata?>? ActiveWorkspaceChanged;
    public event EventHandler<WorkspaceMetadata>? WorkspaceAdded;
    public event EventHandler<WorkspaceMetadata>? WorkspaceRemoved;

    public WorkspaceService(
        WorkspaceRepository repository,
        IPersistenceService persistence,
        ILogger<WorkspaceService> logger)
    {
        _repository = repository;
        _persistence = persistence;
        _logger = logger;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        var workspaces = await _repository.ListAllAsync(ct);
        _workspaces.Clear();
        _workspaces.AddRange(workspaces.Where(w => !w.IsArchived));
        _logger.LogInformation("Loaded {Count} workspaces", _workspaces.Count);
    }

    public async Task<WorkspaceMetadata> CreateWorkspaceAsync(
        string name, string description = "", string colorHex = "#5B9BD5", CancellationToken ct = default)
    {
        var metadata = new WorkspaceMetadata
        {
            Name = name,
            Description = description,
            ColorHex = colorHex,
        };

        await _repository.SaveAsync(metadata, ct);
        _workspaces.Add(metadata);
        WorkspaceAdded?.Invoke(this, metadata);
        _logger.LogInformation("Created workspace '{Name}' ({Id})", name, metadata.Id);
        return metadata;
    }

    public async Task SetActiveWorkspaceAsync(Guid workspaceId, CancellationToken ct = default)
    {
        var workspace = _workspaces.FirstOrDefault(w => w.Id == workspaceId)
            ?? await _repository.GetAsync(workspaceId, ct);

        _activeWorkspace = workspace;
        ActiveWorkspaceChanged?.Invoke(this, _activeWorkspace);
        _logger.LogDebug("Active workspace set to {Name}", _activeWorkspace?.Name);
    }

    public async Task UpdateWorkspaceAsync(WorkspaceMetadata metadata, CancellationToken ct = default)
    {
        metadata.ModifiedAt = DateTime.UtcNow;
        await _repository.SaveAsync(metadata, ct);

        var idx = _workspaces.FindIndex(w => w.Id == metadata.Id);
        if (idx >= 0)
            _workspaces[idx] = metadata;
    }

    public async Task ArchiveWorkspaceAsync(Guid workspaceId, CancellationToken ct = default)
    {
        var meta = _workspaces.FirstOrDefault(w => w.Id == workspaceId);
        if (meta is null)
            return;

        meta.IsArchived = true;
        meta.ArchivedAt = DateTime.UtcNow;
        await _repository.SaveAsync(meta, ct);

        _workspaces.Remove(meta);
        if (_activeWorkspace?.Id == workspaceId)
        {
            _activeWorkspace = _workspaces.FirstOrDefault();
            ActiveWorkspaceChanged?.Invoke(this, _activeWorkspace);
        }
        WorkspaceRemoved?.Invoke(this, meta);
    }

    public async Task DeleteWorkspaceAsync(Guid workspaceId, CancellationToken ct = default)
    {
        var meta = _workspaces.FirstOrDefault(w => w.Id == workspaceId);
        await _repository.DeleteAsync(workspaceId, ct);

        if (meta is not null)
        {
            _workspaces.Remove(meta);
            WorkspaceRemoved?.Invoke(this, meta);
        }

        if (_activeWorkspace?.Id == workspaceId)
        {
            _activeWorkspace = _workspaces.FirstOrDefault();
            ActiveWorkspaceChanged?.Invoke(this, _activeWorkspace);
        }
    }

    public async Task<WorkspaceMetadata> CloneWorkspaceAsync(Guid sourceId, string newName, CancellationToken ct = default)
    {
        var source = _workspaces.FirstOrDefault(w => w.Id == sourceId)
            ?? throw new InvalidOperationException($"Workspace {sourceId} not found");

        var newMeta = new WorkspaceMetadata
        {
            Name = newName,
            Description = source.Description,
            ColorHex = source.ColorHex,
            Icon = source.Icon
        };

        await _repository.SaveAsync(newMeta, ct);

        var sourceTasksDir = _repository.GetTasksFolder(sourceId);
        var destTasksDir = _repository.GetTasksFolder(newMeta.Id);
        if (Directory.Exists(sourceTasksDir))
        {
            await Task.Run(() =>
            {
                foreach (var file in Directory.EnumerateFiles(sourceTasksDir, "*.json"))
                    File.Copy(file, Path.Combine(destTasksDir, Path.GetFileName(file)));
            }, ct);
        }

        _workspaces.Add(newMeta);
        WorkspaceAdded?.Invoke(this, newMeta);
        return newMeta;
    }

    public async Task<WorkspaceSettings> GetWorkspaceSettingsAsync(Guid workspaceId, CancellationToken ct = default)
        => await _repository.GetSettingsAsync(workspaceId, ct);

    public async Task SaveWorkspaceSettingsAsync(Guid workspaceId, WorkspaceSettings settings, CancellationToken ct = default)
        => await _repository.SaveSettingsAsync(workspaceId, settings, ct);

    public Task SyncAsync(CancellationToken ct = default)
        => throw new NotImplementedException("Sync is not yet implemented.");
}
