using System.IO.Compression;
using Microsoft.Extensions.Logging;
using OpenNotes.Interfaces;

namespace OpenNotes.Persistence;

public class BackupService : IBackupService
{
    private readonly WorkspaceRepository _workspaceRepo;
    private readonly ILogger<BackupService> _logger;

    public BackupService(WorkspaceRepository workspaceRepo, ILogger<BackupService> logger)
    {
        _workspaceRepo = workspaceRepo;
        _logger = logger;
    }

    public async Task<BackupEntry> CreateBackupAsync(Guid workspaceId, string? label = null, CancellationToken ct = default)
    {
        var backupsFolder = _workspaceRepo.GetBackupsFolder(workspaceId);
        Directory.CreateDirectory(backupsFolder);

        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-dd_HH-mm-ss");
        var fileName = $"backup_{timestamp}.zip";
        var zipPath = Path.Combine(backupsFolder, fileName);

        var workspaceFolder = _workspaceRepo.GetWorkspaceFolder(workspaceId);

        await Task.Run(() =>
        {
            using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
            var tasksDir = _workspaceRepo.GetTasksFolder(workspaceId);
            if (Directory.Exists(tasksDir))
            {
                foreach (var file in Directory.EnumerateFiles(tasksDir, "*.json"))
                {
                    ct.ThrowIfCancellationRequested();
                    zip.CreateEntryFromFile(file, Path.Combine("tasks", Path.GetFileName(file)));
                }
            }

            var diagDir = _workspaceRepo.GetDiagramsFolder(workspaceId);
            if (Directory.Exists(diagDir))
            {
                foreach (var file in Directory.EnumerateFiles(diagDir, "*.json"))
                {
                    ct.ThrowIfCancellationRequested();
                    zip.CreateEntryFromFile(file, Path.Combine("diagrams", Path.GetFileName(file)));
                }
            }

            var metaPath = _workspaceRepo.GetMetadataPath(workspaceId);
            if (File.Exists(metaPath))
                zip.CreateEntryFromFile(metaPath, "metadata.json");

            var settingsPath = _workspaceRepo.GetSettingsPath(workspaceId);
            if (File.Exists(settingsPath))
                zip.CreateEntryFromFile(settingsPath, "settings.json");
        }, ct);

        var info = new FileInfo(zipPath);
        var entry = new BackupEntry
        {
            FilePath = zipPath,
            CreatedAt = DateTime.UtcNow,
            SizeBytes = info.Length,
            Label = label ?? timestamp
        };

        _logger.LogInformation("Created backup {Path} ({Bytes} bytes)", zipPath, info.Length);
        return entry;
    }

    public async Task RestoreBackupAsync(Guid workspaceId, string backupFilePath, CancellationToken ct = default)
    {
        var workspaceFolder = _workspaceRepo.GetWorkspaceFolder(workspaceId);
        await Task.Run(() =>
        {
            ZipFile.ExtractToDirectory(backupFilePath, workspaceFolder, overwriteFiles: true);
        }, ct);
        _logger.LogInformation("Restored backup {Path} to {Folder}", backupFilePath, workspaceFolder);
    }

    public Task<IReadOnlyList<BackupEntry>> ListBackupsAsync(Guid workspaceId, CancellationToken ct = default)
    {
        var backupsFolder = _workspaceRepo.GetBackupsFolder(workspaceId);
        if (!Directory.Exists(backupsFolder))
            return Task.FromResult<IReadOnlyList<BackupEntry>>([]);

        var entries = Directory.EnumerateFiles(backupsFolder, "backup_*.zip")
            .Select(f =>
            {
                var info = new FileInfo(f);
                return new BackupEntry
                {
                    FilePath = f,
                    CreatedAt = info.CreationTimeUtc,
                    SizeBytes = info.Length,
                    Label = Path.GetFileNameWithoutExtension(f)
                };
            })
            .OrderByDescending(e => e.CreatedAt)
            .ToList();

        return Task.FromResult<IReadOnlyList<BackupEntry>>(entries);
    }

    public Task DeleteBackupAsync(string backupFilePath, CancellationToken ct = default)
    {
        if (File.Exists(backupFilePath))
            File.Delete(backupFilePath);
        return Task.CompletedTask;
    }

    public async Task PruneOldBackupsAsync(Guid workspaceId, int maxKeep, CancellationToken ct = default)
    {
        var entries = await ListBackupsAsync(workspaceId, ct);
        var toDelete = entries.Skip(maxKeep).ToList();
        foreach (var entry in toDelete)
        {
            await DeleteBackupAsync(entry.FilePath, ct);
            _logger.LogDebug("Pruned old backup {Path}", entry.FilePath);
        }
    }
}
