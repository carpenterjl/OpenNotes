using OpenNotes.Models;

namespace OpenNotes.Interfaces;

public class SearchQuery
{
    public string RawQuery { get; set; } = string.Empty;
    public string? TitleFilter { get; set; }
    public List<string> TagFilters { get; set; } = [];
    public TaskStatus? StatusFilter { get; set; }
    public TaskPriority? PriorityFilter { get; set; }
    public DateTime? DueBefore { get; set; }
    public DateTime? DueAfter { get; set; }
    public Guid? WorkspaceId { get; set; }
    public int MaxResults { get; set; } = 50;
}

public interface ISearchService
{
    Task<IReadOnlyList<SearchResult>> SearchAsync(SearchQuery query, CancellationToken ct = default);
    Task<IReadOnlyList<string>> SuggestAsync(string partial, int maxSuggestions = 10, CancellationToken ct = default);
    Task IndexTaskAsync(Guid workspaceId, TaskItem task, CancellationToken ct = default);
    Task RemoveTaskFromIndexAsync(Guid workspaceId, Guid taskId, CancellationToken ct = default);
    Task RebuildIndexAsync(Guid workspaceId, IEnumerable<TaskItem> tasks, CancellationToken ct = default);
}
