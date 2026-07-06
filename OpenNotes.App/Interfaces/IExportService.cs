using OpenNotes.Models;

namespace OpenNotes.Interfaces;

public interface IExportService
{
    Task ExportTaskToMarkdownAsync(TaskItem task, string outputPath, CancellationToken ct = default);
    Task ExportTaskToHtmlAsync(TaskItem task, string outputPath, CancellationToken ct = default);
    Task ExportWorkspaceToZipAsync(Guid workspaceId, string outputPath, CancellationToken ct = default);
}
