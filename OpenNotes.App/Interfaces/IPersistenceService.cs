namespace OpenNotes.Interfaces;

public interface IPersistenceService
{
    Task<T?> ReadAsync<T>(string filePath, CancellationToken ct = default) where T : class;
    Task WriteAsync<T>(string filePath, T data, CancellationToken ct = default) where T : class;
    Task<bool> DeleteAsync(string filePath, CancellationToken ct = default);
    Task<bool> ExistsAsync(string filePath);
    Task<IEnumerable<string>> EnumerateFilesAsync(string directory, string pattern = "*.json");
}
