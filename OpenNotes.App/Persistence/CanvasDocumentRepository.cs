using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using OpenNotes.Models;

namespace OpenNotes.Persistence;

/// <summary>Persistence for multi-page canvas documents stored as <c>.taskcanvas</c> archives
/// (plain ZIP with a renamed extension) under <c>workspaces/{id}/diagrams/{doc-id}.taskcanvas</c>.
///
/// Archive layout:
/// <code>
/// document.json            manifest: metadata, page order, theme colors
/// pages/{pageId}.json      per-page DiagramModel (nodes, connectors, view state)
/// pages/{pageId}.isf       per-page floating ink (WPF Ink Serialized Format), optional
/// pages/{pageId}.thumb.png cached page thumbnail for grid overviews, optional
/// assets/{name}            embedded image files referenced by nodes
/// </code>
///
/// Writes are atomic: the archive is built at <c>path.tmp</c> then renamed over the target.
/// Node images are embedded on save (node <c>ImagePath</c> rewritten to <c>assets/…</c> inside the
/// archive only — the in-memory model is never mutated) and extracted to the workspace cache on
/// load with <c>ImagePath</c> rewritten back to an absolute path, so the rest of the app keeps
/// rendering images from plain file paths.</summary>
public class CanvasDocumentRepository
{
    public const string FileExtension = ".taskcanvas";
    private const string ManifestEntryName = "document.json";
    private const string PagesPrefix = "pages/";
    private const string AssetsPrefix = "assets/";

    // Mirrors JsonPersistenceService so archive JSON round-trips identically to loose files.
    private static readonly JsonSerializerOptions _json = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly WorkspaceRepository _workspaceRepo;
    private readonly ILogger<CanvasDocumentRepository> _logger;

    public CanvasDocumentRepository(WorkspaceRepository workspaceRepo, ILogger<CanvasDocumentRepository> logger)
    {
        _workspaceRepo = workspaceRepo;
        _logger = logger;
    }

    public string GetDocumentPath(Guid workspaceId, Guid documentId)
        => Path.Combine(_workspaceRepo.GetDiagramsFolder(workspaceId), documentId + FileExtension);

    /// <summary>Folder where this document's embedded assets are extracted on load.</summary>
    private string GetAssetCacheFolder(Guid workspaceId, Guid documentId)
        => Path.Combine(_workspaceRepo.GetCacheFolder(workspaceId), "canvas", documentId.ToString("N"), "assets");

    public Task<bool> ExistsAsync(Guid workspaceId, Guid documentId)
        => Task.FromResult(File.Exists(GetDocumentPath(workspaceId, documentId)));

    public Task DeleteAsync(Guid workspaceId, Guid documentId, CancellationToken ct = default)
        => Task.Run(() =>
        {
            var path = GetDocumentPath(workspaceId, documentId);
            if (File.Exists(path)) File.Delete(path);
            var cache = Path.GetDirectoryName(GetAssetCacheFolder(workspaceId, documentId))!;
            if (Directory.Exists(cache)) Directory.Delete(cache, recursive: true);
        }, ct);

    public Task<CanvasDocument?> GetAsync(Guid workspaceId, Guid documentId, CancellationToken ct = default)
        => Task.Run(() =>
        {
            var path = GetDocumentPath(workspaceId, documentId);
            if (!File.Exists(path)) return null;
            try
            {
                using var zip = ZipFile.OpenRead(path);

                var manifest = ReadJsonEntry<CanvasDocumentManifest>(zip, ManifestEntryName);
                if (manifest is null)
                {
                    _logger.LogError("Canvas document {Path} has no readable manifest", path);
                    return null;
                }

                var assetFolder = ExtractAssets(zip, workspaceId, documentId);

                var doc = new CanvasDocument { Manifest = manifest };
                foreach (var info in manifest.Pages)
                {
                    var diagram = ReadJsonEntry<DiagramModel>(zip, $"{PagesPrefix}{info.Id:N}.json");
                    if (diagram is null)
                    {
                        _logger.LogWarning("Canvas document {Id} page {PageId} is missing its payload; using an empty page",
                            documentId, info.Id);
                        diagram = new DiagramModel { Title = info.Title };
                    }

                    // Point embedded image references at the extracted cache copies.
                    foreach (var node in diagram.Nodes)
                    {
                        if (node.ImagePath is not null && node.ImagePath.StartsWith(AssetsPrefix, StringComparison.Ordinal))
                            node.ImagePath = Path.Combine(assetFolder, node.ImagePath[AssetsPrefix.Length..]);
                    }

                    var page = new CanvasPage(info, diagram)
                    {
                        FloatingInk = ReadBinaryEntry(zip, $"{PagesPrefix}{info.Id:N}.isf"),
                        Thumbnail = ReadBinaryEntry(zip, $"{PagesPrefix}{info.Id:N}.thumb.png"),
                    };
                    doc.Pages.Add(page);
                }

                return doc;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to read canvas document {Path}", path);
                return (CanvasDocument?)null;
            }
        }, ct);

    public Task SaveAsync(Guid workspaceId, CanvasDocument doc, CancellationToken ct = default)
        => Task.Run(() =>
        {
            doc.Manifest.ModifiedAt = DateTime.UtcNow;
            // The manifest page list is the single source of page order — rebuild it from Pages
            // so a caller can never persist the two out of sync.
            doc.Manifest.Pages = doc.Pages.Select(p => p.Info).ToList();

            var path = GetDocumentPath(workspaceId, doc.Manifest.Id);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var tmpPath = path + ".tmp";
            try
            {
                using (var stream = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
                {
                    WriteJsonEntry(zip, ManifestEntryName, doc.Manifest);

                    var writtenAssets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var page in doc.Pages)
                    {
                        // Serialize a clone so asset-path rewriting never mutates the live model.
                        var diagram = Clone(page.Diagram);
                        foreach (var node in diagram.Nodes)
                            EmbedNodeImage(zip, node, writtenAssets);

                        WriteJsonEntry(zip, $"{PagesPrefix}{page.Info.Id:N}.json", diagram);
                        if (page.FloatingInk is { Length: > 0 } ink)
                            WriteBinaryEntry(zip, $"{PagesPrefix}{page.Info.Id:N}.isf", ink);
                        if (page.Thumbnail is { Length: > 0 } thumb)
                            WriteBinaryEntry(zip, $"{PagesPrefix}{page.Info.Id:N}.thumb.png", thumb);
                    }
                }

                // Atomic rename — crash-safe on NTFS (same pattern as JsonPersistenceService).
                File.Move(tmpPath, path, overwrite: true);
                _logger.LogDebug("Written canvas document {Path}", path);
            }
            catch
            {
                if (File.Exists(tmpPath))
                {
                    try { File.Delete(tmpPath); } catch { /* ignore cleanup failure */ }
                }
                throw;
            }
        }, ct);

    /// <summary>Reads only the manifests of every <c>.taskcanvas</c> in the workspace (page payloads
    /// are never parsed), for library/list views.</summary>
    public Task<List<CanvasDocumentManifest>> ListManifestsAsync(Guid workspaceId, CancellationToken ct = default)
        => Task.Run(() =>
        {
            var result = new List<CanvasDocumentManifest>();
            var folder = _workspaceRepo.GetDiagramsFolder(workspaceId);
            if (!Directory.Exists(folder)) return result;

            foreach (var file in Directory.EnumerateFiles(folder, "*" + FileExtension, SearchOption.TopDirectoryOnly))
            {
                try
                {
                    using var zip = ZipFile.OpenRead(file);
                    var manifest = ReadJsonEntry<CanvasDocumentManifest>(zip, ManifestEntryName);
                    if (manifest is not null) result.Add(manifest);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Skipping unreadable canvas document {File}", file);
                }
            }

            return result.OrderBy(m => m.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
        }, ct);

    // ----- helpers -----

    /// <summary>Copy a node's image file into <c>assets/</c> (deduped by entry name) and rewrite the
    /// clone's path to the archive-relative form. Missing files keep their original path untouched.</summary>
    private void EmbedNodeImage(ZipArchive zip, DiagramNode node, HashSet<string> writtenAssets)
    {
        if (string.IsNullOrEmpty(node.ImagePath)) return;

        // Stable per-node asset name so re-saves overwrite in place instead of accumulating copies.
        var extension = Path.GetExtension(node.ImagePath);
        var assetName = node.Id.ToString("N") + extension;

        if (!File.Exists(node.ImagePath))
        {
            _logger.LogWarning("Canvas node {NodeId} references missing image {Path}; leaving the path as-is",
                node.Id, node.ImagePath);
            return;
        }

        if (writtenAssets.Add(assetName))
        {
            var entry = zip.CreateEntry(AssetsPrefix + assetName, CompressionLevel.Optimal);
            using var target = entry.Open();
            using var source = File.OpenRead(node.ImagePath);
            source.CopyTo(target);
        }

        node.ImagePath = AssetsPrefix + assetName;
    }

    private string ExtractAssets(ZipArchive zip, Guid workspaceId, Guid documentId)
    {
        var folder = GetAssetCacheFolder(workspaceId, documentId);
        Directory.CreateDirectory(folder);

        foreach (var entry in zip.Entries)
        {
            if (!entry.FullName.StartsWith(AssetsPrefix, StringComparison.Ordinal)) continue;
            var name = entry.FullName[AssetsPrefix.Length..];
            if (string.IsNullOrEmpty(name) || name.Contains('/') || name.Contains('\\')) continue; // no nesting / traversal
            entry.ExtractToFile(Path.Combine(folder, name), overwrite: true);
        }

        return folder;
    }

    private static T? ReadJsonEntry<T>(ZipArchive zip, string entryName) where T : class
    {
        var entry = zip.GetEntry(entryName);
        if (entry is null) return null;
        using var stream = entry.Open();
        return JsonSerializer.Deserialize<T>(stream, _json);
    }

    private static void WriteJsonEntry<T>(ZipArchive zip, string entryName, T data)
    {
        var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
        using var stream = entry.Open();
        JsonSerializer.Serialize(stream, data, _json);
    }

    private static byte[]? ReadBinaryEntry(ZipArchive zip, string entryName)
    {
        var entry = zip.GetEntry(entryName);
        if (entry is null) return null;
        using var stream = entry.Open();
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static void WriteBinaryEntry(ZipArchive zip, string entryName, byte[] data)
    {
        // Ink and PNG payloads are already compact; store uncompressed for speed.
        var entry = zip.CreateEntry(entryName, CompressionLevel.NoCompression);
        using var stream = entry.Open();
        stream.Write(data, 0, data.Length);
    }

    private static DiagramModel Clone(DiagramModel diagram)
        => JsonSerializer.Deserialize<DiagramModel>(JsonSerializer.SerializeToUtf8Bytes(diagram, _json), _json)!;
}
