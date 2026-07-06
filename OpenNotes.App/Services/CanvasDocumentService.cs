using Microsoft.Extensions.Logging;
using OpenNotes.Interfaces;
using OpenNotes.Models;
using OpenNotes.Persistence;

namespace OpenNotes.Services;

public class CanvasDocumentService : ICanvasDocumentService
{
    private readonly CanvasDocumentRepository _repository;
    private readonly DiagramRepository _legacyRepository;
    private readonly ITaskService _taskService;
    private readonly ILogger<CanvasDocumentService> _logger;

    public CanvasDocumentService(
        CanvasDocumentRepository repository,
        DiagramRepository legacyRepository,
        ITaskService taskService,
        ILogger<CanvasDocumentService> logger)
    {
        _repository = repository;
        _legacyRepository = legacyRepository;
        _taskService = taskService;
        _logger = logger;
    }

    public Task<CanvasDocument?> LoadAsync(Guid workspaceId, Guid documentId, CancellationToken ct = default)
        => _repository.GetAsync(workspaceId, documentId, ct);

    public Task SaveAsync(Guid workspaceId, CanvasDocument document, CancellationToken ct = default)
        => _repository.SaveAsync(workspaceId, document, ct);

    public Task DeleteAsync(Guid workspaceId, Guid documentId, CancellationToken ct = default)
        => _repository.DeleteAsync(workspaceId, documentId, ct);

    public async Task<CanvasDocument> GetOrCreateForTaskAsync(Guid workspaceId, TaskItem task, CancellationToken ct = default)
    {
        if (task.CanvasDiagramId is Guid existingId)
        {
            var existing = await _repository.GetAsync(workspaceId, existingId, ct);
            if (existing is not null) return existing;

            var migrated = await TryMigrateLegacyAsync(workspaceId, existingId, task, ct);
            if (migrated is not null) return migrated;

            _logger.LogWarning(
                "Task {TaskId} references missing canvas document {DocumentId}; creating a fresh one",
                task.Id, existingId);
        }

        var doc = new CanvasDocument
        {
            Manifest = { Title = $"Canvas: {task.Title}", OwnerTaskId = task.Id }
        };
        doc.AddPage();
        await _repository.SaveAsync(workspaceId, doc, ct);

        // Link the document to the task and persist the task (fires TaskUpdated).
        task.CanvasDiagramId = doc.Manifest.Id;
        await _taskService.UpdateTaskAsync(workspaceId, task, ct);

        return doc;
    }

    public async Task<CanvasDocument> CreateStandaloneAsync(Guid workspaceId, string title, CancellationToken ct = default)
    {
        var doc = new CanvasDocument { Manifest = { Title = title } };
        doc.AddPage();
        await _repository.SaveAsync(workspaceId, doc, ct);
        return doc;
    }

    public async Task<List<CanvasDocumentManifest>> ListStandaloneAsync(Guid workspaceId, CancellationToken ct = default)
    {
        var all = await _repository.ListManifestsAsync(workspaceId, ct);
        return all.Where(m => m.OwnerTaskId is null).ToList();
    }

    /// <summary>Silently upgrade a pre-multi-page <c>diagrams/{id}.json</c> to a one-page
    /// <c>.taskcanvas</c> document (same id, so the task link is untouched). The legacy file is
    /// deleted only after the new archive is fully written.</summary>
    private async Task<CanvasDocument?> TryMigrateLegacyAsync(
        Guid workspaceId, Guid diagramId, TaskItem task, CancellationToken ct)
    {
        var legacy = await _legacyRepository.GetAsync(workspaceId, diagramId, ct);
        if (legacy is null) return null;

        var doc = new CanvasDocument
        {
            Manifest =
            {
                Id = diagramId,
                Title = legacy.Title,
                OwnerTaskId = task.Id,
                CreatedAt = legacy.CreatedAt,
            }
        };
        var info = new CanvasPageInfo { Title = "Page 1" };
        doc.Manifest.Pages.Add(info);
        doc.Pages.Add(new CanvasPage(info, legacy));

        await _repository.SaveAsync(workspaceId, doc, ct);
        await _legacyRepository.DeleteAsync(workspaceId, diagramId, ct);

        _logger.LogInformation(
            "Migrated legacy diagram {DiagramId} ({NodeCount} nodes) to a .taskcanvas document",
            diagramId, legacy.Nodes.Count);
        return doc;
    }
}
