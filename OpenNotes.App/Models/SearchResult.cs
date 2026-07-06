namespace OpenNotes.Models;

public class SearchResult
{
    public Guid TaskId { get; set; }
    public Guid WorkspaceId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Snippet { get; set; } = string.Empty;
    public TaskStatus Status { get; set; }
    public TaskPriority Priority { get; set; }
    public DateTime? DueDate { get; set; }
    public List<string> Tags { get; set; } = [];
    public double Score { get; set; }
    public string WorkspaceName { get; set; } = string.Empty;
}
