namespace OpenNotes.Interfaces;

public interface IAutosaveService
{
    bool IsEnabled { get; set; }
    int IntervalSeconds { get; set; }
    DateTime? LastSavedAt { get; }
    bool IsDirty { get; }

    void MarkDirty();
    void MarkClean();
    Task SaveNowAsync(CancellationToken ct = default);
}
