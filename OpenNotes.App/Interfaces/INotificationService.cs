using OpenNotes.Models;

namespace OpenNotes.Interfaces;

public interface INotificationService
{
    Task ShowToastAsync(string title, string message, string? tag = null);
    Task ScheduleReminderAsync(TaskItem task, CancellationToken ct = default);
    Task CancelReminderAsync(Guid taskId, CancellationToken ct = default);
    Task CancelAllRemindersAsync(CancellationToken ct = default);
    Task ShowReminderPopupAsync(TaskItem task);
}
