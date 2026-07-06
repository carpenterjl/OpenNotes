using OpenNotes.Models.Blocks;

namespace OpenNotes.Models;

public class TaskItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public TaskStatus Status { get; set; } = TaskStatus.NotStarted;
    public TaskPriority Priority { get; set; } = TaskPriority.None;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DueDate { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? CompletedAt { get; set; }
    public List<string> Tags { get; set; } = [];
    public List<string> Categories { get; set; } = [];
    public Guid? ParentId { get; set; }
    public List<Guid> SubtaskIds { get; set; } = [];
    public List<Guid> DependencyIds { get; set; } = [];
    public RecurrenceRule? Recurrence { get; set; }
    public TimeSpan? EstimatedTime { get; set; }
    public TimeSpan? ActualTime { get; set; }
    public List<string> AttachmentPaths { get; set; } = [];
    public List<ContentBlock> ContentBlocks { get; set; } = [];
    public List<ChecklistItem> Checklist { get; set; } = [];
    public ReminderSettings? Reminder { get; set; }
    public Dictionary<string, string> CustomMetadata { get; set; } = [];
    public int CompletionPercentage { get; set; }
    /// <summary>Id of this task's free-form canvas diagram, if one has been created. Null = none yet.</summary>
    public Guid? CanvasDiagramId { get; set; }
    public bool IsPinned { get; set; }
    public bool IsTemplate { get; set; }
    public string? TemplateId { get; set; }
}
