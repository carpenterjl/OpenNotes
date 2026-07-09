using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using OpenNotes.Interfaces;

namespace OpenNotes.Persistence;

public class JsonPersistenceService : IPersistenceService
{
    private readonly ILogger<JsonPersistenceService> _logger;

    // Per-path write lock: two concurrent writes to the SAME file otherwise race on the final
    // File.Move (FileNotFoundException on a shared tmp, or UnauthorizedAccessException replacing the
    // destination). Serializing per path makes overlapping saves last-writer-wins instead of throwing.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _writeLocks = new(StringComparer.OrdinalIgnoreCase);

    private static SemaphoreSlim WriteLockFor(string filePath) =>
        _writeLocks.GetOrAdd(Path.GetFullPath(filePath), _ => new SemaphoreSlim(1, 1));

    private static readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public JsonPersistenceService(ILogger<JsonPersistenceService> logger)
    {
        _logger = logger;
    }

    public async Task<T?> ReadAsync<T>(string filePath, CancellationToken ct = default) where T : class
    {
        try
        {
            if (!File.Exists(filePath))
                return null;

            // ConfigureAwait(false) throughout this class: callers may block on these tasks from a
            // thread with a SynchronizationContext (the app-settings eager load runs under the WPF
            // dispatcher) — capturing the context here deadlocks such callers.
            var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
            await using var _ = stream.ConfigureAwait(false);
            return await JsonSerializer.DeserializeAsync<T>(stream, _options, ct).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "JSON parse error reading {FilePath}. Attempting recovery.", filePath);
            return TryReadBackup<T>(filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read {FilePath}", filePath);
            return null;
        }
    }

    public async Task WriteAsync<T>(string filePath, T data, CancellationToken ct = default) where T : class
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        // Unique per-write temp name (belt-and-braces with the per-path lock) so no two writes ever
        // share one ".tmp". (Rapid custom-theme color saves used to race on a fixed name.)
        var tmpPath = $"{filePath}.{Guid.NewGuid():N}.tmp";
        var gate = WriteLockFor(filePath);
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var stream = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, true);
            await using (stream.ConfigureAwait(false))
            {
                await JsonSerializer.SerializeAsync(stream, data, _options, ct).ConfigureAwait(false);
            }
            // Atomic rename — crash-safe on NTFS
            File.Move(tmpPath, filePath, overwrite: true);
            _logger.LogDebug("Written {FilePath}", filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write {FilePath}", filePath);
            if (File.Exists(tmpPath))
            {
                try { File.Delete(tmpPath); } catch { /* ignore cleanup failure */ }
            }
            throw;
        }
        finally
        {
            gate.Release();
        }
    }

    public Task<bool> DeleteAsync(string filePath, CancellationToken ct = default)
    {
        try
        {
            if (File.Exists(filePath))
                File.Delete(filePath);
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete {FilePath}", filePath);
            return Task.FromResult(false);
        }
    }

    public Task<bool> ExistsAsync(string filePath)
        => Task.FromResult(File.Exists(filePath));

    public Task<IEnumerable<string>> EnumerateFilesAsync(string directory, string pattern = "*.json")
    {
        if (!Directory.Exists(directory))
            return Task.FromResult(Enumerable.Empty<string>());

        return Task.FromResult<IEnumerable<string>>(
            Directory.EnumerateFiles(directory, pattern, SearchOption.TopDirectoryOnly));
    }

    private T? TryReadBackup<T>(string filePath) where T : class
    {
        // A crashed write leaves a "{name}.{guid}.tmp" beside the target (see WriteAsync). Try the
        // most recent such temp file as a recovery source.
        var directory = Path.GetDirectoryName(filePath);
        var name = Path.GetFileName(filePath);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            return null;

        foreach (var tmpPath in Directory.EnumerateFiles(directory, name + ".*.tmp")
                     .OrderByDescending(File.GetLastWriteTimeUtc))
        {
            try
            {
                using var stream = new FileStream(tmpPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                return JsonSerializer.Deserialize<T>(stream, _options);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Recovery failed for {FilePath}", tmpPath);
            }
        }
        return null;
    }
}
