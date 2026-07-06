using OpenNotes.Models;

namespace OpenNotes.Interfaces;

/// <summary>Multi-page canvas documents (<c>.taskcanvas</c> archives). Documents are either
/// task-owned (opened via the task's Canvas button; <c>Manifest.OwnerTaskId</c> set) or
/// standalone workspace canvases.</summary>
public interface ICanvasDocumentService
{
    Task<CanvasDocument?> LoadAsync(Guid workspaceId, Guid documentId, CancellationToken ct = default);
    Task SaveAsync(Guid workspaceId, CanvasDocument document, CancellationToken ct = default);
    Task DeleteAsync(Guid workspaceId, Guid documentId, CancellationToken ct = default);

    /// <summary>Returns the canvas document for a task, creating and linking one on first use.
    /// A pre-existing single-page legacy diagram (<c>diagrams/{id}.json</c>) is silently migrated
    /// into a one-page document on first open.</summary>
    Task<CanvasDocument> GetOrCreateForTaskAsync(Guid workspaceId, TaskItem task, CancellationToken ct = default);

    /// <summary>Create a standalone (not task-owned) canvas document with one empty page.</summary>
    Task<CanvasDocument> CreateStandaloneAsync(Guid workspaceId, string title, CancellationToken ct = default);

    /// <summary>Manifests of all standalone canvas documents in the workspace (no page payloads read).</summary>
    Task<List<CanvasDocumentManifest>> ListStandaloneAsync(Guid workspaceId, CancellationToken ct = default);
}
