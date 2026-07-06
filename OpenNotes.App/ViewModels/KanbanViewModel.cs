using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using OpenNotes.Interfaces;
using OpenNotes.Models;

namespace OpenNotes.ViewModels;

public class KanbanColumn : ObservableObject
{
    private string _header = string.Empty;
    private TaskStatus _status;

    public string Header { get => _header; set => SetProperty(ref _header, value); }
    public TaskStatus Status { get => _status; set => SetProperty(ref _status, value); }
    public ObservableCollection<TaskItem> Tasks { get; } = [];
}

public partial class KanbanViewModel : ViewModelBase
{
    private readonly IWorkspaceService _workspaceService;
    private readonly ITaskService _taskService;
    private readonly IDialogService _dialogs;
    private readonly INavigationService _navigation;
    private readonly ILogger<KanbanViewModel> _logger;

    public ObservableCollection<KanbanColumn> Columns { get; } = [];

    [ObservableProperty] private KanbanColumn? _selectedColumn;
    [ObservableProperty] private TaskItem? _draggedTask;

    public KanbanViewModel(
        IWorkspaceService workspaceService,
        ITaskService taskService,
        IDialogService dialogs,
        INavigationService navigation,
        ILogger<KanbanViewModel> logger)
    {
        _workspaceService = workspaceService;
        _taskService = taskService;
        _dialogs = dialogs;
        _navigation = navigation;
        _logger = logger;
        Title = "Kanban";

        InitializeColumns();

        _taskService.TaskCreated += (_, t) => App.Current.Dispatcher.Invoke(() => AddToColumn(t));
        _taskService.TaskUpdated += (_, t) => App.Current.Dispatcher.Invoke(() => RefreshTask(t));
        _taskService.TaskDeleted += (_, id) => App.Current.Dispatcher.Invoke(() => RemoveFromColumns(id));
        _workspaceService.ActiveWorkspaceChanged += async (_, ws) =>
        {
            if (ws is not null) await LoadAsync(ws.Id);
        };
    }

    public override async Task InitializeAsync(CancellationToken ct = default)
    {
        if (_workspaceService.ActiveWorkspace is not null)
            await LoadAsync(_workspaceService.ActiveWorkspace.Id, ct);
    }

    [RelayCommand]
    private async Task CreateTaskInColumnAsync(KanbanColumn column)
    {
        if (_workspaceService.ActiveWorkspace is null) return;
        var title = await _dialogs.ShowInputAsync("New Task", "Task title:", "New Task");
        if (string.IsNullOrWhiteSpace(title)) return;
        var task = await _taskService.CreateTaskAsync(_workspaceService.ActiveWorkspace.Id, title);
        task.Status = column.Status;
        await _taskService.UpdateTaskAsync(_workspaceService.ActiveWorkspace.Id, task);
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
    private async Task MoveTaskAsync(object parameter)
    {
        if (parameter is not object[] args || args.Length < 2) return;
        if (args[0] is not TaskItem task || args[1] is not KanbanColumn targetColumn) return;
        if (_workspaceService.ActiveWorkspace is null) return;

        // Remove from source column
        foreach (var col in Columns)
            col.Tasks.Remove(task);

        task.Status = targetColumn.Status;
        targetColumn.Tasks.Add(task);
        await _taskService.UpdateTaskAsync(_workspaceService.ActiveWorkspace.Id, task);
    }

    private void InitializeColumns()
    {
        var statuses = new[]
        {
            (TaskStatus.NotStarted, "Not Started"),
            (TaskStatus.InProgress, "In Progress"),
            (TaskStatus.Blocked, "Blocked"),
            (TaskStatus.Review, "Review"),
            (TaskStatus.Completed, "Completed")
        };

        foreach (var (status, header) in statuses)
        {
            Columns.Add(new KanbanColumn { Status = status, Header = header });
        }
    }

    private async Task LoadAsync(Guid workspaceId, CancellationToken ct = default)
    {
        IsBusy = true;
        try
        {
            var tasks = await _taskService.GetTasksAsync(workspaceId, ct);
            foreach (var col in Columns)
                col.Tasks.Clear();

            foreach (var task in tasks)
                AddToColumn(task);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void AddToColumn(TaskItem task)
    {
        var col = Columns.FirstOrDefault(c => c.Status == task.Status)
                  ?? Columns.First();
        if (!col.Tasks.Contains(task))
            col.Tasks.Add(task);
    }

    private void RefreshTask(TaskItem updated)
    {
        // Remove from all columns first
        RemoveFromColumns(updated.Id);
        AddToColumn(updated);
    }

    private void RemoveFromColumns(Guid taskId)
    {
        foreach (var col in Columns)
        {
            var task = col.Tasks.FirstOrDefault(t => t.Id == taskId);
            if (task is not null)
                col.Tasks.Remove(task);
        }
    }
}
