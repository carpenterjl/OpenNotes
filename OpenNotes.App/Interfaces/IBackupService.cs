namespace OpenNotes.Interfaces;

public class BackupEntry
{
    public string FilePath { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public long SizeBytes { get; set; }
    public string Label { get; set; } = string.Empty;
}

public interface IBackupService
{
    Task<BackupEntry> CreateBackupAsync(Guid workspaceId, string? label = null, CancellationToken ct = default);
    Task RestoreBackupAsync(Guid workspaceId, string backupFilePath, CancellationToken ct = default);
    Task<IReadOnlyList<BackupEntry>> ListBackupsAsync(Guid workspaceId, CancellationToken ct = default);
    Task DeleteBackupAsync(string backupFilePath, CancellationToken ct = default);
    Task PruneOldBackupsAsync(Guid workspaceId, int maxKeep, CancellationToken ct = default);
}
