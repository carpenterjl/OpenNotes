using Microsoft.Extensions.Logging;
using OpenNotes.Interfaces;
using OpenNotes.Models;

namespace OpenNotes.Persistence;

public class WorkspaceRepository
{
    private readonly IPersistenceService _persistence;
    private readonly ILogger<WorkspaceRepository> _logger;
    private readonly string _rootFolder;

    public WorkspaceRepository(
        IPersistenceService persistence,
        ILogger<WorkspaceRepository> logger,
        string? rootFolderOverride = null)
    {
        _persistence = persistence;
        _logger = logger;
        _rootFolder = rootFolderOverride ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "OpenNotes", "workspaces");
        Directory.CreateDirectory(_rootFolder);
    }

    public string RootFolder => _rootFolder;

    public string GetWorkspaceFolder(Guid workspaceId)
        => Path.Combine(_rootFolder, workspaceId.ToString());

    public string GetMetadataPath(Guid workspaceId)
        => Path.Combine(GetWorkspaceFolder(workspaceId), "metadata.json");

    public string GetSettingsPath(Guid workspaceId)
        => Path.Combine(GetWorkspaceFolder(workspaceId), "settings.json");

    public string GetTasksFolder(Guid workspaceId)
        => Path.Combine(GetWorkspaceFolder(workspaceId), "tasks");

    public string GetDiagramsFolder(Guid workspaceId)
        => Path.Combine(GetWorkspaceFolder(workspaceId), "diagrams");

    public string GetAttachmentsFolder(Guid workspaceId)
        => Path.Combine(GetWorkspaceFolder(workspaceId), "attachments");

    public string GetBackupsFolder(Guid workspaceId)
        => Path.Combine(GetWorkspaceFolder(workspaceId), "backups");

    public string GetCacheFolder(Guid workspaceId)
        => Path.Combine(GetWorkspaceFolder(workspaceId), "cache");

    public async Task<List<WorkspaceMetadata>> ListAllAsync(CancellationToken ct = default)
    {
        var result = new List<WorkspaceMetadata>();
        if (!Directory.Exists(_rootFolder))
            return result;

        foreach (var dir in Directory.EnumerateDirectories(_rootFolder))
        {
            var dirName = Path.GetFileName(dir);
            if (!Guid.TryParse(dirName, out var id))
                continue;

            var meta = await _persistence.ReadAsync<WorkspaceMetadata>(GetMetadataPath(id), ct);
            if (meta is not null)
                result.Add(meta);
        }

        return result.OrderBy(w => w.Name).ToList();
    }

    public async Task<WorkspaceMetadata?> GetAsync(Guid workspaceId, CancellationToken ct = default)
        => await _persistence.ReadAsync<WorkspaceMetadata>(GetMetadataPath(workspaceId), ct);

    public async Task SaveAsync(WorkspaceMetadata metadata, CancellationToken ct = default)
    {
        var folder = GetWorkspaceFolder(metadata.Id);
        Directory.CreateDirectory(folder);
        Directory.CreateDirectory(GetTasksFolder(metadata.Id));
        Directory.CreateDirectory(GetDiagramsFolder(metadata.Id));
        Directory.CreateDirectory(GetAttachmentsFolder(metadata.Id));
        Directory.CreateDirectory(GetBackupsFolder(metadata.Id));
        Directory.CreateDirectory(GetCacheFolder(metadata.Id));

        metadata.FolderPath = folder;
        await _persistence.WriteAsync(GetMetadataPath(metadata.Id), metadata, ct);
    }

    public async Task DeleteAsync(Guid workspaceId, CancellationToken ct = default)
    {
        var folder = GetWorkspaceFolder(workspaceId);
        if (Directory.Exists(folder))
        {
            await Task.Run(() => Directory.Delete(folder, recursive: true), ct);
            _logger.LogInformation("Deleted workspace folder {Folder}", folder);
        }
    }

    public async Task<WorkspaceSettings> GetSettingsAsync(Guid workspaceId, CancellationToken ct = default)
        => await _persistence.ReadAsync<WorkspaceSettings>(GetSettingsPath(workspaceId), ct)
           ?? new WorkspaceSettings();

    public async Task SaveSettingsAsync(Guid workspaceId, WorkspaceSettings settings, CancellationToken ct = default)
        => await _persistence.WriteAsync(GetSettingsPath(workspaceId), settings, ct);
}
