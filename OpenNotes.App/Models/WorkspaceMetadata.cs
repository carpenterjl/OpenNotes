namespace OpenNotes.Models;

public class WorkspaceMetadata
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ColorHex { get; set; } = "#5B9BD5";
    public string Icon { get; set; } = "📁";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ArchivedAt { get; set; }
    public bool IsArchived { get; set; }
    public string FolderPath { get; set; } = string.Empty;
    public List<string> FavoritedTaskIds { get; set; } = [];
    public List<string> PinnedTagIds { get; set; } = [];
    public long TotalTaskCount { get; set; }
    public long CompletedTaskCount { get; set; }
}
