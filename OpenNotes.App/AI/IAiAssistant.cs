namespace OpenNotes.AI;

// Extension point for future AI assistant integration
public interface IAiAssistant
{
    Task<string> SuggestTaskTitleAsync(string context, CancellationToken ct = default);
    Task<string> SummarizeTaskAsync(string taskContent, CancellationToken ct = default);
    Task<IReadOnlyList<string>> SuggestTagsAsync(string taskContent, CancellationToken ct = default);
}
