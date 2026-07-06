using Microsoft.Extensions.Logging;
using OpenNotes.Interfaces;
using OpenNotes.Models;

namespace OpenNotes.Search;

// Phase 1 stub — full SQLite FTS5 implementation in Phase 4
public class SearchService : ISearchService
{
    private readonly ITaskService _taskService;
    private readonly ILogger<SearchService> _logger;

    public SearchService(ITaskService taskService, ILogger<SearchService> logger)
    {
        _taskService = taskService;
        _logger = logger;
    }

    public async Task<IReadOnlyList<SearchResult>> SearchAsync(SearchQuery query, CancellationToken ct = default)
    {
        if (query.WorkspaceId is null)
            return [];

        var tasks = await _taskService.GetTasksAsync(query.WorkspaceId.Value, ct);
        var lower = query.RawQuery.ToLowerInvariant();

        return tasks
            .Where(t => string.IsNullOrEmpty(lower) ||
                        t.Title.Contains(lower, StringComparison.OrdinalIgnoreCase) ||
                        t.Description.Contains(lower, StringComparison.OrdinalIgnoreCase))
            .Take(query.MaxResults)
            .Select(t => new SearchResult
            {
                TaskId = t.Id,
                WorkspaceId = query.WorkspaceId.Value,
                Title = t.Title,
                Snippet = t.Description.Length > 100 ? t.Description[..100] + "…" : t.Description,
                Status = t.Status,
                Priority = t.Priority,
                DueDate = t.DueDate,
                Tags = t.Tags,
                Score = 1.0
            })
            .ToList()
            .AsReadOnly();
    }

    public Task<IReadOnlyList<string>> SuggestAsync(string partial, int maxSuggestions = 10, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<string>>([]);

    public Task IndexTaskAsync(Guid workspaceId, TaskItem task, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task RemoveTaskFromIndexAsync(Guid workspaceId, Guid taskId, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task RebuildIndexAsync(Guid workspaceId, IEnumerable<TaskItem> tasks, CancellationToken ct = default)
        => Task.CompletedTask;
}
