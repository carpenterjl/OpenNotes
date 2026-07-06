using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenNotes.Interfaces;

namespace OpenNotes.Notifications;

public class ReminderScheduler : BackgroundService
{
    private readonly ITaskService _taskService;
    private readonly IWorkspaceService _workspaceService;
    private readonly INotificationService _notifications;
    private readonly ILogger<ReminderScheduler> _logger;

    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(1);

    public ReminderScheduler(
        ITaskService taskService,
        IWorkspaceService workspaceService,
        INotificationService notifications,
        ILogger<ReminderScheduler> logger)
    {
        _taskService = taskService;
        _workspaceService = workspaceService;
        _notifications = notifications;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await CheckRemindersAsync(stoppingToken);
            await Task.Delay(CheckInterval, stoppingToken);
        }
    }

    private async Task CheckRemindersAsync(CancellationToken ct)
    {
        try
        {
            var now = DateTime.UtcNow;
            foreach (var workspace in _workspaceService.AllWorkspaces)
            {
                var tasks = await _taskService.GetTasksAsync(workspace.Id, ct);
                foreach (var task in tasks)
                {
                    if (task.Reminder is null || !task.Reminder.IsEnabled) continue;

                    // Check absolute time reminders
                    var reminderTime = task.Reminder.AbsoluteTime;
                    if (reminderTime.HasValue
                        && reminderTime.Value <= now
                        && reminderTime.Value >= now - CheckInterval
                        && task.Reminder.SnoozedUntil.All(s => s.SnoozedUntil < now))
                    {
                        await _notifications.ShowReminderPopupAsync(task);
                        task.Reminder.IsEnabled = false; // Mark as fired
                        await _taskService.UpdateTaskAsync(workspace.Id, task, ct);
                    }
                }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error checking reminders");
        }
    }
}
