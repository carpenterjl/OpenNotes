namespace OpenNotes.Models;

public enum ReminderTrigger { AtTime, MinutesBefore, HoursBefore, DaysBefore }

public class ReminderSettings
{
    public bool IsEnabled { get; set; }
    public ReminderTrigger Trigger { get; set; } = ReminderTrigger.MinutesBefore;
    public int OffsetValue { get; set; } = 30;
    public DateTime? AbsoluteTime { get; set; }
    public bool ShowToast { get; set; } = true;
    public bool ShowPopup { get; set; } = true;
    public List<SnoozeEntry> SnoozedUntil { get; set; } = [];
}

public class SnoozeEntry
{
    public DateTime SnoozedAt { get; set; }
    public DateTime SnoozedUntil { get; set; }
}
