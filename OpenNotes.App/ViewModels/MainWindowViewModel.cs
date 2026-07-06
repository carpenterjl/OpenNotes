using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using OpenNotes.Interfaces;
using OpenNotes.Models;

namespace OpenNotes.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IWorkspaceService _workspaceService;
    private readonly INavigationService _navigation;
    private readonly IThemeService _themeService;
    private readonly ICommandPaletteService _commandPalette;
    private readonly IUndoRedoService _undoRedo;
    private readonly IDialogService _dialogs;
    private readonly ILogger<MainWindowViewModel> _logger;

    [ObservableProperty] private object? _activeContent;
    [ObservableProperty] private WorkspaceMetadata? _activeWorkspace;
    [ObservableProperty] private bool _isCommandPaletteOpen;
    [ObservableProperty] private bool _isSidebarVisible = true;
    [ObservableProperty] private bool _isInspectorVisible = true;
    [ObservableProperty] private string _statusMessage = "Ready";
    [ObservableProperty] private bool _canUndo;
    [ObservableProperty] private bool _canRedo;
    [ObservableProperty] private string _currentTheme = "Dark";
    [ObservableProperty] private string _nextUndoDescription = string.Empty;

    public SidebarViewModel SidebarViewModel { get; }
    public DashboardViewModel DashboardViewModel { get; }

    /// <summary>The command palette engine, bound by the overlay in MainWindow.xaml.</summary>
    public CommandPaletteViewModel Palette { get; }

    public MainWindowViewModel(
        IWorkspaceService workspaceService,
        INavigationService navigation,
        IThemeService themeService,
        ICommandPaletteService commandPalette,
        IUndoRedoService undoRedo,
        IDialogService dialogs,
        SidebarViewModel sidebarViewModel,
        DashboardViewModel dashboardViewModel,
        CommandPaletteViewModel palette,
        ILogger<MainWindowViewModel> logger)
    {
        _workspaceService = workspaceService;
        _navigation = navigation;
        _themeService = themeService;
        _commandPalette = commandPalette;
        _undoRedo = undoRedo;
        _dialogs = dialogs;
        SidebarViewModel = sidebarViewModel;
        DashboardViewModel = dashboardViewModel;
        Palette = palette;
        _logger = logger;

        _workspaceService.ActiveWorkspaceChanged += OnActiveWorkspaceChanged;
        _navigation.CurrentViewChanged += OnCurrentViewChanged;
        _undoRedo.StateChanged += OnUndoRedoStateChanged;
        _commandPalette.VisibilityChanged += OnCommandPaletteVisibilityChanged;
        _themeService.ThemeChanged += (_, t) => CurrentTheme = t;

        CurrentTheme = _themeService.CurrentTheme;
        RegisterCommands();
    }

    public override async Task InitializeAsync(CancellationToken ct = default)
    {
        IsBusy = true;
        try
        {
            await _workspaceService.InitializeAsync(ct);

            if (_workspaceService.AllWorkspaces.Count == 0)
                await _workspaceService.CreateWorkspaceAsync("My Workspace", ct: ct);

            var first = _workspaceService.AllWorkspaces.First();
            await _workspaceService.SetActiveWorkspaceAsync(first.Id, ct);

            await SidebarViewModel.InitializeAsync(ct);
            await DashboardViewModel.InitializeAsync(ct);

            ActiveContent = DashboardViewModel;
            StatusMessage = "Ready";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize");
            StatusMessage = "Error during startup";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void NavigateToDashboard() => ActiveContent = DashboardViewModel;

    [RelayCommand]
    private void ToggleSidebar() => IsSidebarVisible = !IsSidebarVisible;

    [RelayCommand]
    private void ToggleInspector() => IsInspectorVisible = !IsInspectorVisible;

    [RelayCommand]
    private void OpenCommandPalette() => _commandPalette.Show();

    [RelayCommand]
    private void CloseCommandPalette() => _commandPalette.Hide();

    [RelayCommand]
    private void Undo()
    {
        if (_undoRedo.CanUndo)
            _undoRedo.Undo();
    }

    [RelayCommand]
    private void Redo()
    {
        if (_undoRedo.CanRedo)
            _undoRedo.Redo();
    }

    [RelayCommand]
    private void SwitchTheme(string themeName) => _themeService.ApplyTheme(themeName);

    [RelayCommand]
    private void OpenSettings()
    {
        var dialog = new Dialogs.SettingsDialog(_themeService);
        dialog.Owner = System.Windows.Application.Current.MainWindow;
        dialog.ShowDialog();
    }

    [RelayCommand]
    private void CustomizeTheme()
    {
        var dialog = new Dialogs.CustomThemeDialog(_themeService)
        {
            Owner = System.Windows.Application.Current.MainWindow
        };
        dialog.ShowDialog();
    }

    [RelayCommand]
    private async Task CreateWorkspaceAsync()
    {
        var name = await _dialogs.ShowInputAsync("New Workspace", "Enter workspace name:", "New Workspace");
        if (string.IsNullOrWhiteSpace(name)) return;
        var ws = await _workspaceService.CreateWorkspaceAsync(name);
        await _workspaceService.SetActiveWorkspaceAsync(ws.Id);
    }

    private void RegisterCommands()
    {
        _commandPalette.Register(new PaletteCommand
        {
            Id = "nav.dashboard",
            Title = "Go to Dashboard",
            Category = "Navigation",
            KeyboardShortcut = "Ctrl+D",
            Description = "Open the dashboard.",
            ExecuteAsync = async () => { NavigateToDashboard(); await Task.CompletedTask; }
        });
        _commandPalette.Register(new PaletteCommand
        {
            Id = "theme.dark",
            Title = "Switch to Dark Theme",
            Category = "Appearance",
            Description = "Apply the dark theme.",
            ExecuteAsync = async () => { SwitchTheme("Dark"); await Task.CompletedTask; }
        });
        _commandPalette.Register(new PaletteCommand
        {
            Id = "theme.light",
            Title = "Switch to Light Theme",
            Category = "Appearance",
            Description = "Apply the light theme.",
            ExecuteAsync = async () => { SwitchTheme("Light"); await Task.CompletedTask; }
        });
        _commandPalette.Register(new PaletteCommand
        {
            Id = "nav.tasks",
            Title = "Go to Tasks",
            Category = "Navigation",
            Description = "Open the task list.",
            ExecuteAsync = async () => { _navigation.NavigateTo<TaskListViewModel>(); await Task.CompletedTask; }
        });
        _commandPalette.Register(new PaletteCommand
        {
            Id = "nav.kanban",
            Title = "Go to Kanban",
            Category = "Navigation",
            Description = "Open the kanban board.",
            ExecuteAsync = async () => { _navigation.NavigateTo<KanbanViewModel>(); await Task.CompletedTask; }
        });
        _commandPalette.Register(new PaletteCommand
        {
            Id = "nav.search",
            Title = "Go to Search",
            Category = "Navigation",
            KeyboardShortcut = "Ctrl+F",
            Description = "Open the search view.",
            ExecuteAsync = async () => { _navigation.NavigateTo<SearchViewModel>(); await Task.CompletedTask; }
        });
        _commandPalette.Register(new PaletteCommand
        {
            Id = "nav.canvases",
            Title = "Go to Canvases",
            Category = "Navigation",
            Description = "Open the canvas library.",
            ExecuteAsync = async () => { _navigation.NavigateTo<CanvasLibraryViewModel>(); await Task.CompletedTask; }
        });
        _commandPalette.Register(new PaletteCommand
        {
            Id = "app.settings",
            Title = "Open Settings",
            Category = "Application",
            Description = "Open the settings dialog.",
            ExecuteAsync = async () => { OpenSettings(); await Task.CompletedTask; }
        });
        _commandPalette.Register(new PaletteCommand
        {
            Id = "theme.settings",
            Title = "Theme Settings",
            Category = "Appearance",
            Description = "Open the settings dialog to change themes.",
            ExecuteAsync = async () => { OpenSettings(); await Task.CompletedTask; }
        });
        _commandPalette.Register(new PaletteCommand
        {
            Id = "theme.highcontrast",
            Title = "Switch to High-Contrast Theme",
            Category = "Appearance",
            ExecuteAsync = async () => { SwitchTheme("HighContrast"); await Task.CompletedTask; }
        });
        _commandPalette.Register(new PaletteCommand
        {
            Id = "theme.customize",
            Title = "Customize Theme Colors…",
            Category = "Appearance",
            Description = "Edit each color of the Custom theme.",
            ExecuteAsync = async () => { CustomizeTheme(); await Task.CompletedTask; }
        });
        _commandPalette.Register(new PaletteCommand
        {
            Id = "view.sidebar",
            Title = "Toggle Sidebar",
            Category = "View",
            KeyboardShortcut = "Ctrl+B",
            Description = "Show or hide the workspace sidebar.",
            ExecuteAsync = async () => { ToggleSidebar(); await Task.CompletedTask; }
        });
        _commandPalette.Register(new PaletteCommand
        {
            Id = "view.inspector",
            Title = "Toggle Inspector",
            Category = "View",
            Description = "Show or hide the inspector panel.",
            ExecuteAsync = async () => { ToggleInspector(); await Task.CompletedTask; }
        });
        _commandPalette.Register(new PaletteCommand
        {
            Id = "edit.undo",
            Title = "Undo",
            Category = "Edit",
            KeyboardShortcut = "Ctrl+Z",
            Description = "Undo the last action.",
            CanExecute = () => _undoRedo.CanUndo,
            ExecuteAsync = async () => { Undo(); await Task.CompletedTask; }
        });
        _commandPalette.Register(new PaletteCommand
        {
            Id = "edit.redo",
            Title = "Redo",
            Category = "Edit",
            KeyboardShortcut = "Ctrl+Y",
            Description = "Redo the last undone action.",
            CanExecute = () => _undoRedo.CanRedo,
            ExecuteAsync = async () => { Redo(); await Task.CompletedTask; }
        });
    }

    private Guid? _lastWorkspaceId;

    private void OnActiveWorkspaceChanged(object? sender, WorkspaceMetadata? workspace)
    {
        var previous = _lastWorkspaceId;
        _lastWorkspaceId = workspace?.Id;
        ActiveWorkspace = workspace;
        StatusMessage = workspace is not null ? $"Workspace: {workspace.Name}" : "No workspace";

        // Re-home to the Dashboard on a real switch: the current view (task editor, canvas,
        // search results, …) still holds the OLD workspace's data. Skipped for the very first
        // activation (InitializeAsync sets Dashboard itself) and same-workspace re-fires.
        // Swapping ActiveContent unloads the old view, whose Unloaded flush saves pending edits
        // against its load-time workspace id. The back stack points into the old workspace, so
        // it is cleared too. Dashboard/Sidebar reload through their own ActiveWorkspaceChanged
        // subscriptions.
        // (A null workspace — the active one was archived/deleted — still re-homes: the open
        // view's data no longer exists.)
        if (previous is null || previous == workspace?.Id) return;
        NavigateToDashboard();
        _navigation.ClearHistory();
    }

    private void OnCurrentViewChanged(object? sender, object? view) => ActiveContent = view;

    private void OnUndoRedoStateChanged(object? sender, EventArgs e)
    {
        CanUndo = _undoRedo.CanUndo;
        CanRedo = _undoRedo.CanRedo;
        NextUndoDescription = _undoRedo.NextUndoDescription ?? string.Empty;
    }

    private void OnCommandPaletteVisibilityChanged(object? sender, bool visible)
    {
        IsCommandPaletteOpen = visible;
        if (visible)
            _ = Palette.PrepareForShowAsync();
    }

    public override void Cleanup()
    {
        _workspaceService.ActiveWorkspaceChanged -= OnActiveWorkspaceChanged;
        _navigation.CurrentViewChanged -= OnCurrentViewChanged;
        _undoRedo.StateChanged -= OnUndoRedoStateChanged;
        _commandPalette.VisibilityChanged -= OnCommandPaletteVisibilityChanged;
    }
}
