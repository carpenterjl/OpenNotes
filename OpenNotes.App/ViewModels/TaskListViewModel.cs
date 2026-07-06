using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using OpenNotes.Interfaces;
using OpenNotes.Models;

namespace OpenNotes.ViewModels;

public enum TaskListMode { List, Card, Kanban }

/// <summary>Date-based quick filter, driven by the dashboard stat cards.</summary>
public enum TaskDateFilter { None, Overdue, DueToday, Upcoming }

public partial class TaskListViewModel : ViewModelBase
{
    private readonly IWorkspaceService _workspaceService;
    private readonly ITaskService _taskService;
    private readonly IDialogService _dialogs;
    private readonly INavigationService _navigation;
    private readonly ILogger<TaskListViewModel> _logger;

    [ObservableProperty] private TaskListMode _listMode = TaskListMode.List;
    [ObservableProperty] private string _filterText = string.Empty;
    [ObservableProperty] private TaskStatus? _filterStatus;
    [ObservableProperty] private TaskPriority? _filterPriority;
    [ObservableProperty] private string _sortField = "ModifiedAt";
    [ObservableProperty] private bool _sortDescending = true;
    [ObservableProperty] private TaskItem? _selectedTask;
    [ObservableProperty] private bool _showCompleted = true;
    [ObservableProperty] private TaskDateFilter _dateFilter = TaskDateFilter.None;

    public ObservableCollection<TaskItem> Tasks { get; } = [];
    public ObservableCollection<TaskItem> FilteredTasks { get; } = [];

    public TaskListViewModel(
        IWorkspaceService workspaceService,
        ITaskService taskService,
        IDialogService dialogs,
        INavigationService navigation,
        ILogger<TaskListViewModel> logger)
    {
        _workspaceService = workspaceService;
        _taskService = taskService;
        _dialogs = dialogs;
        _navigation = navigation;
        _logger = logger;
        Title = "Tasks";

        _taskService.TaskCreated += (_, t) => App.Current.Dispatcher.Invoke(() => { Tasks.Add(t); ApplyFilter(); });
        _taskService.TaskUpdated += (_, t) => App.Current.Dispatcher.Invoke(() => RefreshTask(t));
        _taskService.TaskDeleted += (_, id) => App.Current.Dispatcher.Invoke(() => RemoveTask(id));
        _workspaceService.ActiveWorkspaceChanged += async (_, ws) =>
        {
            if (ws is not null) await LoadTasksAsync(ws.Id);
        };
    }

    public override async Task InitializeAsync(CancellationToken ct = default)
    {
        if (_workspaceService.ActiveWorkspace is not null)
            await LoadTasksAsync(_workspaceService.ActiveWorkspace.Id, ct);
    }

    [RelayCommand]
    private void OpenTask(TaskItem task)
    {
        if (task is null || _workspaceService.ActiveWorkspace is null) return;
        _navigation.NavigateTo<TaskEditorViewModel>(vm =>
        {
            _ = vm.LoadTaskAsync(_workspaceService.ActiveWorkspace.Id, task.Id);
        });
    }

    [RelayCommand]
    private async Task CreateTaskAsync()
    {
        if (_workspaceService.ActiveWorkspace is null) return;
        var title = await _dialogs.ShowInputAsync("New Task", "Task title:", "Untitled Task");
        if (string.IsNullOrWhiteSpace(title)) return;
        await _taskService.CreateTaskAsync(_workspaceService.ActiveWorkspace.Id, title);
    }

    [RelayCommand]
    private async Task DeleteTaskAsync(TaskItem task)
    {
        if (task is null || _workspaceService.ActiveWorkspace is null) return;
        var confirm = await _dialogs.ShowConfirmAsync("Delete Task", $"Delete '{task.Title}'?", "Delete");
        if (!confirm) return;
        await _taskService.DeleteTaskAsync(_workspaceService.ActiveWorkspace.Id, task.Id);
    }

    [RelayCommand]
    private async Task CompleteTaskAsync(TaskItem task)
    {
        if (task is null || _workspaceService.ActiveWorkspace is null) return;
        task.Status = TaskStatus.Completed;
        task.CompletedAt = DateTime.UtcNow;
        task.CompletionPercentage = 100;
        await _taskService.UpdateTaskAsync(_workspaceService.ActiveWorkspace.Id, task);
    }

    [RelayCommand]
    private void SetListMode(string mode)
    {
        ListMode = Enum.Parse<TaskListMode>(mode);
    }

    partial void OnFilterTextChanged(string value) => ApplyFilter();
    partial void OnFilterStatusChanged(TaskStatus? value) => ApplyFilter();
    partial void OnFilterPriorityChanged(TaskPriority? value) => ApplyFilter();
    partial void OnShowCompletedChanged(bool value) => ApplyFilter();
    partial void OnDateFilterChanged(TaskDateFilter value) => ApplyFilter();
    partial void OnSortFieldChanged(string value) => ApplyFilter();
    partial void OnSortDescendingChanged(bool value) => ApplyFilter();

    private async Task LoadTasksAsync(Guid workspaceId, CancellationToken ct = default)
    {
        IsBusy = true;
        try
        {
            var tasks = await _taskService.GetTasksAsync(workspaceId, ct);
            Tasks.Clear();
            foreach (var t in tasks)
                Tasks.Add(t);
            ApplyFilter();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyFilter()
    {
        var query = FilterText.ToLowerInvariant();
        var filtered = Tasks.AsEnumerable();

        if (!string.IsNullOrEmpty(query))
            filtered = filtered.Where(t =>
                t.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                t.Description.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                t.Tags.Any(tag => tag.Contains(query, StringComparison.OrdinalIgnoreCase)));

        if (FilterStatus.HasValue)
            filtered = filtered.Where(t => t.Status == FilterStatus.Value);

        if (FilterPriority.HasValue)
            filtered = filtered.Where(t => t.Priority == FilterPriority.Value);

        if (DateFilter != TaskDateFilter.None)
        {
            var now = DateTime.UtcNow;
            var todayEnd = now.Date.AddDays(1).AddTicks(-1);
            var weekEnd = now.Date.AddDays(7);
            filtered = DateFilter switch
            {
                TaskDateFilter.Overdue => filtered.Where(t => t.DueDate.HasValue && t.DueDate.Value < now
                    && t.Status != TaskStatus.Completed && t.Status != TaskStatus.Cancelled),
                TaskDateFilter.DueToday => filtered.Where(t => t.DueDate.HasValue
                    && t.DueDate.Value >= now && t.DueDate.Value <= todayEnd),
                TaskDateFilter.Upcoming => filtered.Where(t => t.DueDate.HasValue
                    && t.DueDate.Value > todayEnd && t.DueDate.Value <= weekEnd),
                _ => filtered
            };
        }

        if (!ShowCompleted)
            filtered = filtered.Where(t => t.Status != TaskStatus.Completed && t.Status != TaskStatus.Cancelled);

        filtered = SortField switch
        {
            "Title" => SortDescending ? filtered.OrderByDescending(t => t.Title) : filtered.OrderBy(t => t.Title),
            "Priority" => SortDescending ? filtered.OrderByDescending(t => t.Priority) : filtered.OrderBy(t => t.Priority),
            "DueDate" => SortDescending ? filtered.OrderByDescending(t => t.DueDate) : filtered.OrderBy(t => t.DueDate),
            "Status" => SortDescending ? filtered.OrderByDescending(t => t.Status) : filtered.OrderBy(t => t.Status),
            _ => SortDescending ? filtered.OrderByDescending(t => t.ModifiedAt) : filtered.OrderBy(t => t.ModifiedAt)
        };

        FilteredTasks.Clear();
        foreach (var t in filtered)
            FilteredTasks.Add(t);
    }

    private void RefreshTask(TaskItem updated)
    {
        var idx = Tasks.IndexOf(Tasks.FirstOrDefault(t => t.Id == updated.Id)!);
        if (idx >= 0) Tasks[idx] = updated;
        ApplyFilter();
    }

    private void RemoveTask(Guid id)
    {
        var t = Tasks.FirstOrDefault(x => x.Id == id);
        if (t is not null) Tasks.Remove(t);
        ApplyFilter();
    }
}
