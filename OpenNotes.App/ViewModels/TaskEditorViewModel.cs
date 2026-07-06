using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using OpenNotes.Interfaces;
using OpenNotes.Models;
using OpenNotes.Models.Blocks;
using OpenNotes.ViewModels.Blocks;

namespace OpenNotes.ViewModels;

public partial class TaskEditorViewModel : ViewModelBase
{
    private readonly ITaskService _taskService;
    private readonly IUndoRedoService _undoRedo;
    private readonly IAutosaveService _autosave;
    private readonly INavigationService _navigation;
    private readonly BlockViewModelFactory _blockFactory;
    private readonly ILogger<TaskEditorViewModel> _logger;

    private TaskItem? _originalTask;

    /// <summary>The workspace the task was LOADED from. Saves must target this id, not the
    /// currently active workspace — after a workspace switch a deferred/unload save would
    /// otherwise write the task into the wrong workspace.</summary>
    private Guid _workspaceId;

    [ObservableProperty] private Guid _taskId;
    [ObservableProperty] private string _taskTitle = string.Empty;
    [ObservableProperty] private string _description = string.Empty;
    [ObservableProperty] private TaskStatus _status = TaskStatus.NotStarted;
    [ObservableProperty] private TaskPriority _priority = TaskPriority.None;
    [ObservableProperty] private DateTime? _dueDate;
    [ObservableProperty] private DateTime? _startDate;
    [ObservableProperty] private int _completionPercentage;
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private bool _isEditingTitle;
    [ObservableProperty] private string _tagInput = string.Empty;

    public ObservableCollection<BlockViewModelBase> BlockViewModels { get; } = [];
    public ObservableCollection<ChecklistItem> Checklist { get; } = [];
    public ObservableCollection<string> Tags { get; } = [];

    public TaskEditorViewModel(
        ITaskService taskService,
        IUndoRedoService undoRedo,
        IAutosaveService autosave,
        INavigationService navigation,
        BlockViewModelFactory blockFactory,
        ILogger<TaskEditorViewModel> logger)
    {
        _taskService = taskService;
        _undoRedo = undoRedo;
        _autosave = autosave;
        _navigation = navigation;
        _blockFactory = blockFactory;
        _logger = logger;
    }

    public async Task LoadTaskAsync(Guid workspaceId, Guid taskId, CancellationToken ct = default)
    {
        IsBusy = true;
        try
        {
            var task = await _taskService.GetTaskAsync(workspaceId, taskId, ct);
            if (task is null) return;

            _workspaceId = workspaceId;
            _originalTask = task;
            TaskId = task.Id;
            Title = $"Task: {task.Title}";
            TaskTitle = task.Title;
            Description = task.Description;
            Status = task.Status;
            Priority = task.Priority;
            DueDate = task.DueDate;
            StartDate = task.StartDate;
            CompletionPercentage = task.CompletionPercentage;

            BlockViewModels.Clear();
            foreach (var block in task.ContentBlocks.OrderBy(b => b.Order))
                AddBlockViewModel(_blockFactory.Create(block));

            Checklist.Clear();
            foreach (var item in task.Checklist.OrderBy(c => c.Order))
                Checklist.Add(item);

            Tags.Clear();
            foreach (var tag in task.Tags)
                Tags.Add(tag);

            IsDirty = false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (_originalTask is null || _workspaceId == Guid.Empty) return;

        _originalTask.Title = TaskTitle;
        _originalTask.Description = Description;
        _originalTask.Status = Status;
        _originalTask.Priority = Priority;
        _originalTask.DueDate = DueDate;
        _originalTask.StartDate = StartDate;
        _originalTask.CompletionPercentage = CompletionPercentage;
        // Keep completion metadata consistent so the dashboard's "Completed" stat reflects
        // editor-driven status changes (mirrors TaskListViewModel.CompleteTaskAsync). Runs after the
        // assignments above so it wins over the raw CompletionPercentage copy.
        if (Status == TaskStatus.Completed && _originalTask.CompletedAt is null)
        {
            _originalTask.CompletedAt = DateTime.UtcNow;
            _originalTask.CompletionPercentage = 100;
        }
        else if (Status != TaskStatus.Completed)
        {
            _originalTask.CompletedAt = null;
        }
        _originalTask.ContentBlocks = BlockViewModels.Select(vm =>
        {
            var block = vm.GetUpdatedBlock();
            vm.PersistHeight();
            return block;
        }).ToList();
        _originalTask.Checklist = [.. Checklist];
        _originalTask.Tags = [.. Tags];

        await _taskService.UpdateTaskAsync(_workspaceId, _originalTask);
        IsDirty = false;
        _autosave.MarkClean();
    }

    /// <summary>Called from the view's Unloaded (navigation away, workspace switch, app close):
    /// flush pending edits so nothing is lost. Fire-and-forget with logging — Unloaded can't await.</summary>
    public void OnViewUnloaded()
    {
        if (!IsDirty || _originalTask is null || _workspaceId == Guid.Empty) return;
        _ = SaveOnUnloadAsync();
    }

    private async Task SaveOnUnloadAsync()
    {
        try
        {
            await SaveAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save task {TaskId} while leaving the editor.", TaskId);
        }
    }

    [RelayCommand]
    private async Task OpenCanvasAsync()
    {
        if (_originalTask is null || _workspaceId == Guid.Empty) return;
        await SaveAsync(); // persist editor edits before the round-trip
        var wsId = _workspaceId;
        var taskId = TaskId;
        _navigation.NavigateTo<CanvasEditorViewModel>(vm => _ = vm.LoadAsync(wsId, taskId));
    }

    [RelayCommand]
    private void AddMarkdownBlock() => AddNewBlock(new MarkdownBlock { Markdown = "" });

    [RelayCommand]
    private void AddCodeBlock() => AddNewBlock(new CodeBlock { Language = "csharp" });

    [RelayCommand]
    private void AddLatexBlock() => AddNewBlock(new LatexBlock { Formula = @"E = mc^2" });

    [RelayCommand]
    private void AddTextBlock() => AddNewBlock(new TextBlock());

    [RelayCommand]
    private void AddMermaidBlock() => AddNewBlock(new MermaidBlock { Definition = "graph TD\n    A --> B" });

    [RelayCommand]
    private void AddChecklistBlockCommand() => AddNewBlock(new ChecklistBlock());

    [RelayCommand]
    private void AddTag()
    {
        var tag = TagInput.Trim();
        if (string.IsNullOrEmpty(tag) || Tags.Contains(tag)) return;
        Tags.Add(tag);
        TagInput = string.Empty;
        MarkDirty();
    }

    [RelayCommand]
    private void RemoveTag(string tag)
    {
        Tags.Remove(tag);
        MarkDirty();
    }

    [RelayCommand]
    private void AddChecklistItem()
    {
        Checklist.Add(new ChecklistItem { Order = Checklist.Count, Text = "New item" });
        MarkDirty();
    }

    [RelayCommand]
    private void RemoveChecklistItem(ChecklistItem item)
    {
        Checklist.Remove(item);
        MarkDirty();
    }

    partial void OnTaskTitleChanged(string value) => MarkDirty();
    partial void OnDescriptionChanged(string value) => MarkDirty();
    partial void OnStatusChanged(TaskStatus value) => MarkDirty();
    partial void OnPriorityChanged(TaskPriority value) => MarkDirty();
    partial void OnDueDateChanged(DateTime? value) => MarkDirty();

    private void AddNewBlock(ContentBlock block)
    {
        block.Order = BlockViewModels.Count;
        AddBlockViewModel(_blockFactory.Create(block));
        MarkDirty();
    }

    private void AddBlockViewModel(BlockViewModelBase vm)
    {
        vm.DeleteRequested += (_, b) => { BlockViewModels.Remove(b); ReorderBlocks(); MarkDirty(); };
        vm.MoveRequested += (_, e) =>
        {
            var idx = BlockViewModels.IndexOf(e.Block);
            var newIdx = idx + e.Direction;
            if (newIdx >= 0 && newIdx < BlockViewModels.Count)
                BlockViewModels.Move(idx, newIdx);
            ReorderBlocks();
            MarkDirty();
        };
        vm.ContentChanged += (_, _) => MarkDirty();
        BlockViewModels.Add(vm);
    }

    private void MarkDirty()
    {
        IsDirty = true;
        _autosave.MarkDirty();
    }

    private void ReorderBlocks()
    {
        for (int i = 0; i < BlockViewModels.Count; i++)
            BlockViewModels[i].Block.Order = i;
    }
}
