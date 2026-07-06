using Microsoft.Extensions.Logging;
using OpenNotes.Interfaces;
using OpenNotes.Models;

namespace OpenNotes.Persistence;

/// <summary>Atomic JSON persistence for free-form canvas diagrams under
/// <c>workspaces/{id}/diagrams/{diagram-id}.json</c>. Mirrors <see cref="TaskRepository"/>.</summary>
public class DiagramRepository
{
    private readonly IPersistenceService _persistence;
    private readonly WorkspaceRepository _workspaceRepo;
    private readonly ILogger<DiagramRepository> _logger;

    public DiagramRepository(
        IPersistenceService persistence,
        WorkspaceRepository workspaceRepo,
        ILogger<DiagramRepository> logger)
    {
        _persistence = persistence;
        _workspaceRepo = workspaceRepo;
        _logger = logger;
    }

    private string GetDiagramPath(Guid workspaceId, Guid diagramId)
        => Path.Combine(_workspaceRepo.GetDiagramsFolder(workspaceId), $"{diagramId}.json");

    public async Task<DiagramModel?> GetAsync(Guid workspaceId, Guid diagramId, CancellationToken ct = default)
        => await _persistence.ReadAsync<DiagramModel>(GetDiagramPath(workspaceId, diagramId), ct);

    public async Task SaveAsync(Guid workspaceId, DiagramModel diagram, CancellationToken ct = default)
    {
        diagram.ModifiedAt = DateTime.UtcNow;
        await _persistence.WriteAsync(GetDiagramPath(workspaceId, diagram.Id), diagram, ct);
    }

    public async Task DeleteAsync(Guid workspaceId, Guid diagramId, CancellationToken ct = default)
        => await _persistence.DeleteAsync(GetDiagramPath(workspaceId, diagramId), ct);

    public async Task<bool> ExistsAsync(Guid workspaceId, Guid diagramId)
        => await _persistence.ExistsAsync(GetDiagramPath(workspaceId, diagramId));
}
