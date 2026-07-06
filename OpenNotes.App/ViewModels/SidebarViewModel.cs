using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using OpenNotes.Interfaces;
using OpenNotes.Models;

namespace OpenNotes.ViewModels;

public partial class SidebarViewModel : ViewModelBase
{
    private readonly IWorkspaceService _workspaceService;
    private readonly INavigationService _navigation;
    private readonly IDialogService _dialogs;
    private readonly ILogger<SidebarViewModel> _logger;

    [ObservableProperty] private WorkspaceMetadata? _selectedWorkspace;
    [ObservableProperty] private string _searchQuery = string.Empty;

    public ObservableCollection<WorkspaceMetadata> Workspaces { get; } = [];
    public ObservableCollection<string> RecentItems { get; } = [];
    public ObservableCollection<string> AllTags { get; } = [];

    public SidebarViewModel(
        IWorkspaceService workspaceService,
        INavigationService navigation,
        IDialogService dialogs,
        ILogger<SidebarViewModel> logger)
    {
        _workspaceService = workspaceService;
        _navigation = navigation;
        _dialogs = dialogs;
        _logger = logger;

        _workspaceService.WorkspaceAdded += OnWorkspaceAdded;
        _workspaceService.WorkspaceRemoved += OnWorkspaceRemoved;
        _workspaceService.ActiveWorkspaceChanged += OnActiveWorkspaceChanged;
    }

    public override Task InitializeAsync(CancellationToken ct = default)
    {
        Workspaces.Clear();
        foreach (var ws in _workspaceService.AllWorkspaces)
            Workspaces.Add(ws);

        SelectedWorkspace = _workspaceService.ActiveWorkspace;
        return Task.CompletedTask;
    }

    [RelayCommand]
    private void NavigateToTasks() => _navigation.NavigateTo<TaskListViewModel>();

    [RelayCommand]
    private void NavigateToKanban() => _navigation.NavigateTo<KanbanViewModel>();

    [RelayCommand]
    private void NavigateToCanvases() => _navigation.NavigateTo<CanvasLibraryViewModel>();

    [RelayCommand]
    private void NavigateToSearch() => _navigation.NavigateTo<SearchViewModel>();

    [RelayCommand]
    private async Task SelectWorkspaceAsync(WorkspaceMetadata workspace)
    {
        if (workspace is null) return;
        await _workspaceService.SetActiveWorkspaceAsync(workspace.Id);
        SelectedWorkspace = workspace;
    }

    [RelayCommand]
    private async Task CreateWorkspaceAsync()
    {
        var name = await _dialogs.ShowInputAsync("New Workspace", "Workspace name:", "New Workspace");
        if (string.IsNullOrWhiteSpace(name)) return;
        await _workspaceService.CreateWorkspaceAsync(name);
    }

    [RelayCommand]
    private async Task ArchiveWorkspaceAsync(WorkspaceMetadata workspace)
    {
        if (workspace is null) return;
        var confirm = await _dialogs.ShowConfirmAsync(
            "Archive Workspace",
            $"Archive '{workspace.Name}'? It will be hidden but not deleted.",
            "Archive", "Cancel");
        if (!confirm) return;
        await _workspaceService.ArchiveWorkspaceAsync(workspace.Id);
    }

    [RelayCommand]
    private async Task DeleteWorkspaceAsync(WorkspaceMetadata workspace)
    {
        if (workspace is null) return;
        var confirm = await _dialogs.ShowConfirmAsync(
            "Delete Workspace",
            $"Permanently delete '{workspace.Name}' and all its tasks?",
            "Delete", "Cancel");
        if (!confirm) return;
        await _workspaceService.DeleteWorkspaceAsync(workspace.Id);
    }

    private void OnWorkspaceAdded(object? sender, WorkspaceMetadata ws)
    {
        App.Current.Dispatcher.Invoke(() => Workspaces.Add(ws));
    }

    private void OnWorkspaceRemoved(object? sender, WorkspaceMetadata ws)
    {
        App.Current.Dispatcher.Invoke(() => Workspaces.Remove(ws));
    }

    private void OnActiveWorkspaceChanged(object? sender, WorkspaceMetadata? ws)
    {
        App.Current.Dispatcher.Invoke(() => SelectedWorkspace = ws);
    }

    public override void Cleanup()
    {
        _workspaceService.WorkspaceAdded -= OnWorkspaceAdded;
        _workspaceService.WorkspaceRemoved -= OnWorkspaceRemoved;
        _workspaceService.ActiveWorkspaceChanged -= OnActiveWorkspaceChanged;
    }
}
