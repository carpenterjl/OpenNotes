using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using OpenNotes.Interfaces;
using OpenNotes.Models;

namespace OpenNotes.ViewModels;

public partial class DashboardViewModel : ViewModelBase
{
    private readonly IWorkspaceService _workspaceService;
    private readonly ITaskService _taskService;
    private readonly INavigationService _navigation;
    private readonly ILogger<DashboardViewModel> _logger;

    [ObservableProperty] private int _dueTodayCount;
    [ObservableProperty] private int _overdueCount;
    [ObservableProperty] private int _upcomingCount;
    [ObservableProperty] private int _completedThisWeekCount;
    [ObservableProperty] private int _totalTaskCount;
    [ObservableProperty] private string _workspaceName = string.Empty;

    public ObservableCollection<TaskItem> DueTodayTasks { get; } = [];
    public ObservableCollection<TaskItem> OverdueTasks { get; } = [];
    public ObservableCollection<TaskItem> UpcomingTasks { get; } = [];
    public ObservableCollection<TaskItem> RecentTasks { get; } = [];

    public DashboardViewModel(
        IWorkspaceService workspaceService,
        ITaskService taskService,
        INavigationService navigation,
        ILogger<DashboardViewModel> logger)
    {
        _workspaceService = workspaceService;
        _taskService = taskService;
        _navigation = navigation;
        _logger = logger;
        Title = "Dashboard";

        _workspaceService.ActiveWorkspaceChanged += async (_, ws) =>
        {
            if (ws is not null)
                await LoadDataAsync(ws);
        };

        // Keep the stat counts live: recompute whenever a task changes anywhere in the app.
        // (Without this, completing a task or changing a due date in the editor leaves the
        // dashboard counts stale until a manual refresh or a fresh forward-navigation.)
        _taskService.TaskCreated += (_, _) => RefreshOnUiThread();
        _taskService.TaskUpdated += (_, _) => RefreshOnUiThread();
        _taskService.TaskDeleted += (_, _) => RefreshOnUiThread();
    }

    /// <summary>Marshal a dashboard reload onto the UI thread (task events may fire off-thread).</summary>
    private void RefreshOnUiThread()
    {
        var ws = _workspaceService.ActiveWorkspace;
        if (ws is null) return;
        var app = App.Current;
        if (app is null) { _ = LoadDataAsync(ws); return; } // no Application (unit tests): run directly
        app.Dispatcher.Invoke(() => _ = LoadDataAsync(ws));
    }

    public override async Task InitializeAsync(CancellationToken ct = default)
    {
        if (_workspaceService.ActiveWorkspace is not null)
            await LoadDataAsync(_workspaceService.ActiveWorkspace, ct);
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (_workspaceService.ActiveWorkspace is not null)
            await LoadDataAsync(_workspaceService.ActiveWorkspace);
    }

    [RelayCommand]
    private void OpenTask(TaskItem? task)
    {
        if (task is null || _workspaceService.ActiveWorkspace is null) return;
        _navigation.NavigateTo<TaskEditorViewModel>(vm =>
            _ = vm.LoadTaskAsync(_workspaceService.ActiveWorkspace.Id, task.Id));
    }

    /// <summary>Navigate to the Task list, pre-filtered to the clicked stat card's category.</summary>
    [RelayCommand]
    private void ShowCategory(string? category)
    {
        _navigation.NavigateTo<TaskListViewModel>(vm =>
        {
            switch (category)
            {
                case "Overdue": vm.DateFilter = TaskDateFilter.Overdue; break;
                case "DueToday": vm.DateFilter = TaskDateFilter.DueToday; break;
                case "Upcoming": vm.DateFilter = TaskDateFilter.Upcoming; break;
                case "Completed": vm.FilterStatus = TaskStatus.Completed; break;
            }
        });
    }

    private bool _reloading;

    private async Task LoadDataAsync(WorkspaceMetadata workspace, CancellationToken ct = default)
    {
        if (_reloading) return; // coalesce overlapping task-event reloads
        _reloading = true;
        IsBusy = true;
        try
        {
            WorkspaceName = workspace.Name;
            // Bucket by local calendar day: DueDate comes from a local-midnight DatePicker, so
            // comparing against DateTime.UtcNow (a specific instant) mis-sorts "due today" tasks
            // into Overdue. CompletedAt is stored in UTC and is compared in UTC below.
            var today = DateTime.Now.Date;
            var weekEnd = today.AddDays(7);

            var tasks = await _taskService.GetTasksAsync(workspace.Id, ct);

            DueTodayTasks.Clear();
            OverdueTasks.Clear();
            UpcomingTasks.Clear();
            RecentTasks.Clear();

            int completedThisWeek = 0;
            foreach (var t in tasks)
            {
                if (t.DueDate.HasValue)
                {
                    var due = t.DueDate.Value.Date;
                    if (due < today && t.Status != TaskStatus.Completed && t.Status != TaskStatus.Cancelled)
                        OverdueTasks.Add(t);
                    else if (due == today)
                        DueTodayTasks.Add(t);
                    else if (due > today && due <= weekEnd)
                        UpcomingTasks.Add(t);
                }

                if (t.Status == TaskStatus.Completed && t.CompletedAt.HasValue &&
                    t.CompletedAt.Value >= DateTime.UtcNow.AddDays(-7))
                    completedThisWeek++;
            }

            // Recent tasks (last 10 modified)
            foreach (var t in tasks.OrderByDescending(x => x.ModifiedAt).Take(10))
                RecentTasks.Add(t);

            DueTodayCount = DueTodayTasks.Count;
            OverdueCount = OverdueTasks.Count;
            UpcomingCount = UpcomingTasks.Count;
            CompletedThisWeekCount = completedThisWeek;
            TotalTaskCount = tasks.Count;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load dashboard data");
        }
        finally
        {
            IsBusy = false;
            _reloading = false;
        }
    }
}
