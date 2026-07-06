using OpenNotes.Models;

namespace OpenNotes.Interfaces;

public interface IWorkspaceService
{
    WorkspaceMetadata? ActiveWorkspace { get; }
    IReadOnlyList<WorkspaceMetadata> AllWorkspaces { get; }

    event EventHandler<WorkspaceMetadata?>? ActiveWorkspaceChanged;
    event EventHandler<WorkspaceMetadata>? WorkspaceAdded;
    event EventHandler<WorkspaceMetadata>? WorkspaceRemoved;

    Task InitializeAsync(CancellationToken ct = default);
    Task<WorkspaceMetadata> CreateWorkspaceAsync(string name, string description = "", string colorHex = "#5B9BD5", CancellationToken ct = default);
    Task SetActiveWorkspaceAsync(Guid workspaceId, CancellationToken ct = default);
    Task UpdateWorkspaceAsync(WorkspaceMetadata metadata, CancellationToken ct = default);
    Task ArchiveWorkspaceAsync(Guid workspaceId, CancellationToken ct = default);
    Task DeleteWorkspaceAsync(Guid workspaceId, CancellationToken ct = default);
    Task<WorkspaceMetadata> CloneWorkspaceAsync(Guid sourceId, string newName, CancellationToken ct = default);
    Task<WorkspaceSettings> GetWorkspaceSettingsAsync(Guid workspaceId, CancellationToken ct = default);
    Task SaveWorkspaceSettingsAsync(Guid workspaceId, WorkspaceSettings settings, CancellationToken ct = default);
    Task SyncAsync(CancellationToken ct = default); // stub for future sync
}
