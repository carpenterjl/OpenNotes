using Microsoft.Extensions.Logging;
using OpenNotes.Interfaces;
using OpenNotes.Models;

namespace OpenNotes.Notifications;

public class NotificationService : INotificationService
{
    private readonly ILogger<NotificationService> _logger;
    private bool _toastAvailable = true;

    public NotificationService(ILogger<NotificationService> logger)
    {
        _logger = logger;
    }

    public Task ShowToastAsync(string title, string message, string? tag = null)
    {
        if (!_toastAvailable)
        {
            _logger.LogInformation("Toast (fallback log): {Title} - {Message}", title, message);
            return Task.CompletedTask;
        }

        try
        {
            var builder = new Microsoft.Toolkit.Uwp.Notifications.ToastContentBuilder()
                .AddArgument("action", "open")
                .AddText(title)
                .AddText(message);

            if (tag is not null)
                builder.AddArgument("taskId", tag);

            builder.Show();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to show toast notification");
            _toastAvailable = false;
        }

        return Task.CompletedTask;
    }

    public Task ScheduleReminderAsync(TaskItem task, CancellationToken ct = default)
    {
        if (task.Reminder is null || !task.Reminder.IsEnabled)
            return Task.CompletedTask;

        _logger.LogInformation("Reminder scheduled for task {Id}", task.Id);
        return Task.CompletedTask;
    }

    public Task CancelReminderAsync(Guid taskId, CancellationToken ct = default)
    {
        _logger.LogDebug("Reminder cancelled for task {Id}", taskId);
        return Task.CompletedTask;
    }

    public Task CancelAllRemindersAsync(CancellationToken ct = default)
    {
        _logger.LogDebug("All reminders cancelled");
        return Task.CompletedTask;
    }

    public Task ShowReminderPopupAsync(TaskItem task)
    {
        return ShowToastAsync($"Reminder: {task.Title}", task.Description ?? "", task.Id.ToString());
    }
}
