namespace OpenNotes.Models;

public class WorkspaceSettings
{
    public string DefaultView { get; set; } = "List";
    public string DefaultSortField { get; set; } = "CreatedAt";
    public bool SortDescending { get; set; } = true;
    public bool GroupByStatus { get; set; } = false;
    public bool ShowCompletedTasks { get; set; } = true;
    public int AutosaveIntervalSeconds { get; set; } = 30;
    public int MaxBackups { get; set; } = 10;
    public List<string> KanbanColumns { get; set; } =
    [
        "NotStarted", "InProgress", "Review", "Completed"
    ];
    public Dictionary<string, string> CustomColumnLabels { get; set; } = [];
}
