namespace OpenNotes.Models;

public enum TaskStatus
{
    NotStarted,
    InProgress,
    Blocked,
    Waiting,
    Review,
    Completed,
    Cancelled,
    Deferred
}

public enum TaskPriority
{
    None,
    Low,
    Medium,
    High,
    Critical
}
