using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using OpenNotes.Interfaces;
using OpenNotes.Models;
using OpenNotes.Services;

namespace OpenNotes.ViewModels;

/// <summary>Visual state of a token chip in the argument-entry header.</summary>
public enum ArgChipState { Filled, Current, Pending }

/// <summary>One chip in the guided-argument header strip: a committed value, the active token, or a
/// pending <c>&lt;Placeholder&gt;</c>.</summary>
public class ArgChip
{
    public string Text { get; init; } = string.Empty;
    public ArgChipState State { get; init; }
}

/// <summary>A single dropdown row. Renders either a command (search mode) or an argument suggestion
/// (guided-entry mode) through one DataTemplate.</summary>
public class PaletteRow
{
    public string Display { get; init; } = string.Empty;
    public string? Category { get; init; }
    public string? Shortcut { get; init; }
    public string? Hint { get; init; }
    public string? Description { get; init; }

    public PaletteCommand? Command { get; init; }
    public PaletteArgOption? Option { get; init; }
    public bool IsCommand => Command is not null;
}

/// <summary>The command palette's brain: a two-mode (command-search / guided argument-entry) filter
/// and executor over <see cref="ICommandPaletteService"/>. WPF-free so it can be unit-tested; the
/// view is a thin binding + key-routing layer. Also registers the data/parameterized command
/// catalog (New Task, Open Canvas, Theme Set, …) that needs the domain services.</summary>
public partial class CommandPaletteViewModel : ViewModelBase
{
    private readonly ICommandPaletteService _service;
    private readonly INavigationService _navigation;
    private readonly ITaskService _taskService;
    private readonly ICanvasDocumentService _canvasService;
    private readonly IWorkspaceService _workspaceService;
    private readonly IThemeService _themeService;
    private readonly IDialogService _dialogs;
    private readonly ILogger<CommandPaletteViewModel> _logger;

    // --- guided-argument state ---
    private PaletteCommand? _activeCommand;
    private readonly List<PaletteArgValue> _filledArgs = [];

    // --- entity snapshots (refreshed on PrepareForShowAsync; read by Options delegates) ---
    private IReadOnlyList<TaskItem> _tasks = [];
    private IReadOnlyList<CanvasDocumentManifest> _canvases = [];
    private IReadOnlyList<WorkspaceMetadata> _workspaces = [];

    [ObservableProperty] private string _query = string.Empty;
    [ObservableProperty] private PaletteRow? _selectedRow;

    public ObservableCollection<PaletteRow> Rows { get; } = [];
    public ObservableCollection<ArgChip> ArgChips { get; } = [];

    public bool IsArgMode => _activeCommand is not null;
    public string ActiveCommandTitle => _activeCommand?.Title ?? string.Empty;

    private IReadOnlyList<PaletteArg> ActiveArgs => _activeCommand?.Args ?? [];
    private int CurrentArgIndex => _filledArgs.Count;

    public string InputPlaceholder => !IsArgMode
        ? "Type a command…"
        : CurrentArgIndex < ActiveArgs.Count
            ? $"Enter <{ActiveArgs[CurrentArgIndex].Name}>…"
            : "Press Enter to run";

    public CommandPaletteViewModel(
        ICommandPaletteService service,
        INavigationService navigation,
        ITaskService taskService,
        ICanvasDocumentService canvasService,
        IWorkspaceService workspaceService,
        IThemeService themeService,
        IDialogService dialogs,
        ILogger<CommandPaletteViewModel> logger)
    {
        _service = service;
        _navigation = navigation;
        _taskService = taskService;
        _canvasService = canvasService;
        _workspaceService = workspaceService;
        _themeService = themeService;
        _dialogs = dialogs;
        _logger = logger;

        RegisterDataCommands();
        Rebuild();
    }

    private Guid? ActiveWorkspaceId => _workspaceService.ActiveWorkspace?.Id;

    // ---------------------------------------------------------------- lifecycle

    /// <summary>Called each time the palette opens: reset to search mode and refresh entity snapshots
    /// so Open/Delete autofill lists are current for the active workspace.</summary>
    public async Task PrepareForShowAsync(CancellationToken ct = default)
    {
        ResetToSearch();
        _workspaces = _workspaceService.AllWorkspaces;
        if (ActiveWorkspaceId is Guid id)
        {
            try { _tasks = await _taskService.GetTasksAsync(id, ct); }
            catch (Exception ex) { _logger.LogWarning(ex, "Palette: failed to load tasks snapshot"); }
            try { _canvases = await _canvasService.ListStandaloneAsync(id, ct); }
            catch (Exception ex) { _logger.LogWarning(ex, "Palette: failed to load canvases snapshot"); }
        }
        Rebuild();
    }

    /// <summary>Back-compat entry point (was called by the old view). Re-applies the current filter.</summary>
    public void RefreshCommands() => Rebuild();

    public void Reset() => ResetToSearch();

    private void ResetToSearch()
    {
        _activeCommand = null;
        _filledArgs.Clear();
        Query = string.Empty;
        Rebuild();
    }

    partial void OnQueryChanged(string value) => Rebuild();

    // ---------------------------------------------------------------- rendering

    private void Rebuild()
    {
        Rows.Clear();
        if (IsArgMode) BuildArgSuggestions(Query);
        else BuildCommandRows(Query);

        SelectedRow = Rows.Count > 0 ? Rows[0] : null;

        BuildChips();
        OnPropertyChanged(nameof(IsArgMode));
        OnPropertyChanged(nameof(ActiveCommandTitle));
        OnPropertyChanged(nameof(InputPlaceholder));
    }

    private void BuildCommandRows(string q)
    {
        var all = _service.AllCommands.Where(c => c.CanExecute?.Invoke() != false);

        IEnumerable<PaletteCommand> ordered;
        if (string.IsNullOrWhiteSpace(q))
        {
            ordered = all.OrderBy(c => c.Category).ThenBy(c => c.Title);
        }
        else
        {
            ordered = all
                .Where(c => c.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                            c.Category?.Contains(q, StringComparison.OrdinalIgnoreCase) == true)
                .OrderBy(c => c.Title.StartsWith(q, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(c => c.Title);
        }

        foreach (var c in ordered.Take(20))
            Rows.Add(new PaletteRow
            {
                Display = c.Title,
                Category = c.Category,
                Shortcut = c.KeyboardShortcut,
                Hint = c.ArgumentHint,
                Description = c.Description,
                Command = c,
            });
    }

    private void BuildArgSuggestions(string q)
    {
        if (CurrentArgIndex >= ActiveArgs.Count) return; // all tokens entered; nothing to suggest
        var arg = ActiveArgs[CurrentArgIndex];
        var opts = arg.Options?.Invoke() ?? [];
        if (!string.IsNullOrWhiteSpace(q))
            opts = opts.Where(o => o.Display.Contains(q, StringComparison.OrdinalIgnoreCase));

        foreach (var o in opts.Take(20))
            Rows.Add(new PaletteRow
            {
                Display = o.Display,
                Category = arg.Name,
                Option = o,
            });
    }

    private void BuildChips()
    {
        ArgChips.Clear();
        if (!IsArgMode) return;

        for (int i = 0; i < ActiveArgs.Count; i++)
        {
            var arg = ActiveArgs[i];
            string bracket = $"<{arg.Name}>";
            if (i < _filledArgs.Count)
            {
                var raw = _filledArgs[i].Raw;
                bool empty = string.IsNullOrWhiteSpace(raw);
                ArgChips.Add(new ArgChip { Text = empty ? bracket : raw, State = empty ? ArgChipState.Pending : ArgChipState.Filled });
            }
            else if (i == CurrentArgIndex)
            {
                ArgChips.Add(new ArgChip { Text = string.IsNullOrWhiteSpace(Query) ? bracket : Query, State = ArgChipState.Current });
            }
            else
            {
                ArgChips.Add(new ArgChip { Text = bracket, State = ArgChipState.Pending });
            }
        }
    }

    // ---------------------------------------------------------------- interaction

    /// <summary>Arrow-key move within the dropdown.</summary>
    public void MoveSelectionUp()
    {
        if (Rows.Count == 0) return;
        var idx = SelectedRow is null ? 0 : Rows.IndexOf(SelectedRow);
        SelectedRow = Rows[Math.Max(0, idx - 1)];
    }

    public void MoveSelectionDown()
    {
        if (Rows.Count == 0) return;
        var idx = SelectedRow is null ? -1 : Rows.IndexOf(SelectedRow);
        SelectedRow = Rows[Math.Min(Rows.Count - 1, idx + 1)];
    }

    /// <summary>TAB — accept the top/selected result. In search mode: enter the command (guided args)
    /// or run it. In arg mode: commit the current token and advance.</summary>
    public void AcceptTop()
    {
        if (!IsArgMode)
        {
            var row = SelectedRow ?? (Rows.Count > 0 ? Rows[0] : null);
            if (row?.Command is { } cmd) BeginOrRunCommand(cmd);
            return;
        }
        CommitCurrentToken();
    }

    /// <summary>Click / activate a specific row.</summary>
    [RelayCommand]
    private void AcceptRow(PaletteRow? row)
    {
        if (row is null) return;
        if (row.Command is { } cmd)
        {
            BeginOrRunCommand(cmd);
            return;
        }
        if (row.Option is { } opt)
        {
            SelectedRow = row;
            CommitCurrentToken();
        }
    }

    /// <summary>ENTER — commit any in-progress token, then run the command if all required args are
    /// filled. In search mode, behaves like <see cref="AcceptTop"/>.</summary>
    public void Submit()
    {
        if (!IsArgMode)
        {
            AcceptTop();
            return;
        }

        bool hasInput = SelectedRow?.Option is not null || !string.IsNullOrWhiteSpace(Query);
        if (hasInput && CurrentArgIndex < ActiveArgs.Count)
            CommitCurrentToken();

        if (IsReadyToRun())
            _ = ExecuteActiveAsync();
    }

    /// <summary>ESC — step out of guided-argument mode back to command search; if already searching,
    /// close the palette.</summary>
    public void EscapeOrClose()
    {
        if (IsArgMode) ResetToSearch();
        else _service.Hide();
    }

    /// <summary>Backspace on an empty token — pop the last committed token back into the box for
    /// editing, or drop out of argument mode entirely.</summary>
    public void StepBack()
    {
        if (!IsArgMode) return;
        if (_filledArgs.Count > 0)
        {
            var last = _filledArgs[^1];
            _filledArgs.RemoveAt(_filledArgs.Count - 1);
            Query = last.Raw;   // triggers Rebuild via OnQueryChanged
            Rebuild();
        }
        else
        {
            ResetToSearch();
        }
    }

    private void BeginOrRunCommand(PaletteCommand cmd)
    {
        if (cmd.Args is { Count: > 0 })
        {
            _activeCommand = cmd;
            _filledArgs.Clear();
            Query = string.Empty;
            Rebuild();
        }
        else
        {
            _ = ExecuteLeafAsync(cmd);
        }
    }

    private void CommitCurrentToken()
    {
        if (CurrentArgIndex >= ActiveArgs.Count) return;
        var arg = ActiveArgs[CurrentArgIndex];

        if (SelectedRow?.Option is { } opt)
        {
            _filledArgs.Add(new PaletteArgValue(opt.Value, opt.Tag));
        }
        else
        {
            var raw = Query.Trim();
            if (arg.Required && raw.Length == 0) return; // can't skip a required arg
            _filledArgs.Add(new PaletteArgValue(raw));
        }

        Query = string.Empty;   // triggers Rebuild
        Rebuild();              // ensure rebuild even when Query was already empty
    }

    private bool IsReadyToRun()
    {
        if (_activeCommand?.Args is not { } args) return false;
        for (int i = 0; i < args.Count; i++)
            if (args[i].Required && (i >= _filledArgs.Count || string.IsNullOrWhiteSpace(_filledArgs[i].Raw)))
                return false;
        return true;
    }

    private async Task ExecuteActiveAsync()
    {
        var cmd = _activeCommand;
        if (cmd?.ExecuteWithArgsAsync is null) return;

        // Pad to the full arg count so executors can index positionally.
        var values = new List<PaletteArgValue>(_filledArgs);
        while (values.Count < (cmd.Args?.Count ?? 0))
            values.Add(new PaletteArgValue(string.Empty));

        _service.Hide();
        ResetToSearch();
        try { await cmd.ExecuteWithArgsAsync(values); }
        catch (Exception ex) { _logger.LogError(ex, "Palette command '{Id}' failed", cmd.Id); }
    }

    private async Task ExecuteLeafAsync(PaletteCommand cmd)
    {
        _service.Hide();
        ResetToSearch();
        if (cmd.ExecuteAsync is null) return;
        try { await cmd.ExecuteAsync(); }
        catch (Exception ex) { _logger.LogError(ex, "Palette command '{Id}' failed", cmd.Id); }
    }

    // ---------------------------------------------------------------- catalog

    private void RegisterDataCommands()
    {
        _service.Register(new PaletteCommand
        {
            Id = "task.new",
            Title = "New Task",
            Category = "Tasks",
            Description = "Create a task and open it. Optional priority, progress %, and deadline.",
            Args =
            [
                Arg("Name", PaletteArgKind.FreeText, required: true),
                Arg("Priority", PaletteArgKind.Choice, options: PriorityOptions),
                Arg("Progress", PaletteArgKind.Number, options: ProgressOptions),
                Arg("Deadline", PaletteArgKind.Date, options: DateOptions),
            ],
            ExecuteWithArgsAsync = NewTaskAsync,
        });

        _service.Register(new PaletteCommand
        {
            Id = "canvas.new",
            Title = "New Canvas",
            Category = "Canvas",
            Description = "Create a standalone canvas. Optional page size and number of pages.",
            Args =
            [
                Arg("Name", PaletteArgKind.FreeText, required: true),
                Arg("Page Size", PaletteArgKind.Choice, options: PageSizeOptions),
                Arg("Pages", PaletteArgKind.Number, options: PageCountOptions),
            ],
            ExecuteWithArgsAsync = NewCanvasAsync,
        });

        _service.Register(new PaletteCommand
        {
            Id = "task.open",
            Title = "Open Task",
            Category = "Tasks",
            Description = "Open an existing task in this workspace.",
            Args = [Arg("Task Name", PaletteArgKind.Entity, required: true, options: TaskOptions)],
            ExecuteWithArgsAsync = OpenTaskAsync,
        });

        _service.Register(new PaletteCommand
        {
            Id = "canvas.open",
            Title = "Open Canvas",
            Category = "Canvas",
            Description = "Open an existing standalone canvas in this workspace.",
            Args = [Arg("Canvas Name", PaletteArgKind.Entity, required: true, options: CanvasOptions)],
            ExecuteWithArgsAsync = OpenCanvasAsync,
        });

        _service.Register(new PaletteCommand
        {
            Id = "search.run",
            Title = "Search",
            Category = "Navigation",
            Description = "Search tasks in this workspace.",
            Args = [Arg("Query", PaletteArgKind.FreeText, required: true)],
            ExecuteWithArgsAsync = RunSearchAsync,
        });

        _service.Register(new PaletteCommand
        {
            Id = "workspace.open",
            Title = "Open Workspace",
            Category = "Workspace",
            Description = "Switch to another workspace.",
            Args = [Arg("Workspace Name", PaletteArgKind.Entity, required: true, options: WorkspaceOptions)],
            ExecuteWithArgsAsync = OpenWorkspaceAsync,
        });

        _service.Register(new PaletteCommand
        {
            Id = "workspace.create",
            Title = "New Workspace",
            Category = "Workspace",
            Description = "Create a new workspace and switch to it.",
            Args = [Arg("Name", PaletteArgKind.FreeText, required: true)],
            ExecuteWithArgsAsync = NewWorkspaceAsync,
        });

        _service.Register(new PaletteCommand
        {
            Id = "entity.delete",
            Title = "Delete",
            Category = "Workspace",
            Description = "Delete a task, canvas, or workspace (asks for confirmation).",
            Args =
            [
                Arg("Type", PaletteArgKind.Choice, required: true, options: DeleteTypeOptions),
                Arg("Name", PaletteArgKind.Entity, required: true, options: DeleteTargetOptions),
            ],
            ExecuteWithArgsAsync = DeleteEntityAsync,
        });

        _service.Register(new PaletteCommand
        {
            Id = "theme.set",
            Title = "Theme Set",
            Category = "Appearance",
            Description = "Switch the application theme (Dark, Light, High Contrast, or Custom).",
            Args = [Arg("Theme", PaletteArgKind.Choice, required: true, options: ThemeOptions)],
            ExecuteWithArgsAsync = SetThemeAsync,
        });

        _service.Register(new PaletteCommand
        {
            Id = "theme.color",
            Title = "Set Theme Color",
            Category = "Appearance",
            Description = "Override one color of the Custom theme by hex (switches to the Custom theme).",
            Args =
            [
                Arg("Item", PaletteArgKind.Choice, required: true, options: ThemeColorItemOptions),
                Arg("Color", PaletteArgKind.FreeText, required: true, options: NamedColorOptions),
            ],
            ExecuteWithArgsAsync = SetThemeColorAsync,
        });

        _service.Register(new PaletteCommand
        {
            Id = "theme.export",
            Title = "Export Theme",
            Category = "Appearance",
            Description = "Save the Custom theme colors to a file.",
            ExecuteAsync = ExportThemeAsync,
        });

        _service.Register(new PaletteCommand
        {
            Id = "theme.import",
            Title = "Import Theme",
            Category = "Appearance",
            Description = "Load Custom theme colors from a file (unknown keys skipped; missing keys use defaults).",
            ExecuteAsync = ImportThemeAsync,
        });
    }

    private static PaletteArg Arg(string name, PaletteArgKind kind, bool required = false,
        Func<IEnumerable<PaletteArgOption>>? options = null)
        => new() { Name = name, Kind = kind, Required = required, Options = options };

    // --- option providers ---

    private static IEnumerable<PaletteArgOption> PriorityOptions() =>
        new[] { "None", "Low", "Medium", "High", "Critical" }.Select(p => new PaletteArgOption(p, p));

    private static IEnumerable<PaletteArgOption> ProgressOptions() =>
        new[] { 0, 25, 50, 75, 100 }.Select(n => new PaletteArgOption($"{n}%", n.ToString()));

    private static IEnumerable<PaletteArgOption> DateOptions()
    {
        var t = DateTime.Today;
        yield return new PaletteArgOption("Today", t.ToString("yyyy-MM-dd"));
        yield return new PaletteArgOption("Tomorrow", t.AddDays(1).ToString("yyyy-MM-dd"));
        yield return new PaletteArgOption("Next week", t.AddDays(7).ToString("yyyy-MM-dd"));
    }

    private static IEnumerable<PaletteArgOption> PageSizeOptions() =>
        new[] { "Default", "A4 Landscape", "A4 Portrait", "Letter", "Large" }
            .Select(s => new PaletteArgOption(s, s));

    private static IEnumerable<PaletteArgOption> PageCountOptions() =>
        new[] { 1, 2, 3, 4, 5 }.Select(n => new PaletteArgOption(n.ToString(), n.ToString()));

    private static IEnumerable<PaletteArgOption> DeleteTypeOptions() =>
        new[] { "Task", "Canvas", "Workspace" }.Select(s => new PaletteArgOption(s, s));

    private IEnumerable<PaletteArgOption> ThemeOptions() =>
        _themeService.AvailableThemes.Select(t => new PaletteArgOption(t, t));

    private IEnumerable<PaletteArgOption> ThemeColorItemOptions() =>
        _themeService.CustomColorItems.Select(i => new PaletteArgOption(i.Label, i.Label));

    /// <summary>Options for the <c>Set Theme Color</c> hex arg: a <c>Choose…</c> entry that opens the
    /// visual color picker, then a handful of named-color shortcuts; the user can also free-type a hex.</summary>
    private static IEnumerable<PaletteArgOption> NamedColorOptions()
    {
        yield return new PaletteArgOption("Choose… (open color picker)", ChooseColorSentinel);
        foreach (var (name, hex) in new[]
        {
            ("Blue", "#7B9CDF"), ("Green", "#A3BE8C"), ("Orange", "#D08770"), ("Red", "#BF616A"),
            ("Yellow", "#EBCB8B"), ("Purple", "#B48EAD"), ("White", "#FFFFFF"), ("Black", "#1E1E2E"),
        })
            yield return new PaletteArgOption($"{name} ({hex})", hex);
    }

    private const string ChooseColorSentinel = "Choose…";
    private const string ThemeFileFilter = "OpenNotes Theme|*.theme.json|JSON|*.json|All Files|*.*";

    private IEnumerable<PaletteArgOption> TaskOptions() =>
        _tasks.Select(t => new PaletteArgOption(t.Title, t.Title, t.Id));

    private IEnumerable<PaletteArgOption> CanvasOptions() =>
        _canvases.Select(c => new PaletteArgOption(c.Title, c.Title, c.Id));

    private IEnumerable<PaletteArgOption> WorkspaceOptions() =>
        _workspaces.Select(w => new PaletteArgOption(w.Name, w.Name, w.Id));

    private IEnumerable<PaletteArgOption> DeleteTargetOptions()
    {
        var type = _filledArgs.Count > 0 ? _filledArgs[0].Raw : string.Empty;
        return type switch
        {
            "Task" => TaskOptions(),
            "Canvas" => CanvasOptions(),
            "Workspace" => WorkspaceOptions(),
            _ => [],
        };
    }

    // --- executors ---

    private async Task NewTaskAsync(IReadOnlyList<PaletteArgValue> args)
    {
        if (ActiveWorkspaceId is not Guid ws) return;
        var name = args[0].Raw.Trim();
        if (name.Length == 0) return;

        var task = await _taskService.CreateTaskAsync(ws, name);
        bool changed = false;
        if (args.Count > 1 && TryParsePriority(args[1].Raw, out var pr)) { task.Priority = pr; changed = true; }
        if (args.Count > 2 && TryParseProgress(args[2].Raw, out var pct)) { task.CompletionPercentage = pct; changed = true; }
        if (args.Count > 3 && TryParseDate(args[3].Raw, out var due)) { task.DueDate = due; changed = true; }
        if (changed) await _taskService.UpdateTaskAsync(ws, task);

        _navigation.NavigateTo<TaskEditorViewModel>(vm => _ = vm.LoadTaskAsync(ws, task.Id));
    }

    private async Task NewCanvasAsync(IReadOnlyList<PaletteArgValue> args)
    {
        if (ActiveWorkspaceId is not Guid ws) return;
        var name = args[0].Raw.Trim();
        if (name.Length == 0) return;

        var doc = await _canvasService.CreateStandaloneAsync(ws, name);
        if (args.Count > 1 && ResolvePageSize(args[1].Raw) is (double w, double h) && doc.Pages.Count > 0)
        {
            doc.Pages[0].Diagram.CanvasWidth = w;
            doc.Pages[0].Diagram.CanvasHeight = h;
        }
        if (args.Count > 2 && int.TryParse(args[2].Raw, out var pages))
            for (int i = 1; i < Math.Clamp(pages, 1, 20); i++)
                doc.AddPage();

        await _canvasService.SaveAsync(ws, doc);
        _navigation.NavigateTo<CanvasEditorViewModel>(vm => _ = vm.LoadStandaloneAsync(ws, doc.Manifest.Id));
    }

    private Task OpenTaskAsync(IReadOnlyList<PaletteArgValue> args)
    {
        if (ActiveWorkspaceId is not Guid ws) return Task.CompletedTask;
        var id = ResolveId(args[0], _tasks.FirstOrDefault(t =>
            string.Equals(t.Title, args[0].Raw, StringComparison.OrdinalIgnoreCase))?.Id);
        if (id is Guid tid)
            _navigation.NavigateTo<TaskEditorViewModel>(vm => _ = vm.LoadTaskAsync(ws, tid));
        return Task.CompletedTask;
    }

    private Task OpenCanvasAsync(IReadOnlyList<PaletteArgValue> args)
    {
        if (ActiveWorkspaceId is not Guid ws) return Task.CompletedTask;
        var id = ResolveId(args[0], _canvases.FirstOrDefault(c =>
            string.Equals(c.Title, args[0].Raw, StringComparison.OrdinalIgnoreCase))?.Id);
        if (id is Guid cid)
            _navigation.NavigateTo<CanvasEditorViewModel>(vm => _ = vm.LoadStandaloneAsync(ws, cid));
        return Task.CompletedTask;
    }

    private Task RunSearchAsync(IReadOnlyList<PaletteArgValue> args)
    {
        var q = args[0].Raw.Trim();
        if (q.Length == 0) return Task.CompletedTask;
        _navigation.NavigateTo<SearchViewModel>(vm => vm.Query = q);
        return Task.CompletedTask;
    }

    private async Task OpenWorkspaceAsync(IReadOnlyList<PaletteArgValue> args)
    {
        var id = ResolveId(args[0], _workspaces.FirstOrDefault(w =>
            string.Equals(w.Name, args[0].Raw, StringComparison.OrdinalIgnoreCase))?.Id);
        if (id is Guid wid) await _workspaceService.SetActiveWorkspaceAsync(wid);
    }

    private async Task NewWorkspaceAsync(IReadOnlyList<PaletteArgValue> args)
    {
        var name = args[0].Raw.Trim();
        if (name.Length == 0) return;
        var ws = await _workspaceService.CreateWorkspaceAsync(name);
        await _workspaceService.SetActiveWorkspaceAsync(ws.Id);
    }

    private async Task DeleteEntityAsync(IReadOnlyList<PaletteArgValue> args)
    {
        var type = args[0].Raw;
        var target = args[1];
        if (string.IsNullOrWhiteSpace(target.Raw)) return;

        var confirmed = await _dialogs.ShowConfirmAsync(
            $"Delete {type}",
            $"Delete {type.ToLowerInvariant()} \"{target.Raw}\"? This cannot be undone.",
            "Delete", "Cancel");
        if (!confirmed) return;

        switch (type)
        {
            case "Task" when ActiveWorkspaceId is Guid ws1 &&
                             ResolveId(target, _tasks.FirstOrDefault(t => t.Title == target.Raw)?.Id) is Guid tid:
                await _taskService.DeleteTaskAsync(ws1, tid);
                break;
            case "Canvas" when ActiveWorkspaceId is Guid ws2 &&
                               ResolveId(target, _canvases.FirstOrDefault(c => c.Title == target.Raw)?.Id) is Guid cid:
                await _canvasService.DeleteAsync(ws2, cid);
                break;
            case "Workspace" when ResolveId(target, _workspaces.FirstOrDefault(w => w.Name == target.Raw)?.Id) is Guid wid:
                await _workspaceService.DeleteWorkspaceAsync(wid);
                break;
        }
    }

    private Task SetThemeAsync(IReadOnlyList<PaletteArgValue> args)
    {
        var theme = args[0].Raw;
        if (_themeService.AvailableThemes.Contains(theme))
            _themeService.ApplyTheme(theme);
        return Task.CompletedTask;
    }

    private async Task SetThemeColorAsync(IReadOnlyList<PaletteArgValue> args)
    {
        var label = args[0].Raw.Trim();
        var raw = args[1].Raw.Trim();
        var item = _themeService.CustomColorItems
            .FirstOrDefault(i => string.Equals(i.Label, label, StringComparison.OrdinalIgnoreCase));
        if (item is null)
        {
            _logger.LogWarning("Set Theme Color: unknown item '{Label}'", label);
            return;
        }

        var hex = raw;
        // "Choose…" (or bare "Choose") opens the visual picker seeded with the item's current color.
        if (raw.Equals(ChooseColorSentinel, StringComparison.OrdinalIgnoreCase)
            || raw.Equals("Choose", StringComparison.OrdinalIgnoreCase))
        {
            var current = _themeService.GetCustomColors().GetValueOrDefault(item.Key);
            var picked = await _dialogs.ShowColorPickerAsync(current);
            if (picked is null) return; // cancelled
            hex = picked;
        }

        _themeService.SetCustomColor(item.Key, hex);
    }

    private async Task ExportThemeAsync()
    {
        var path = await _dialogs.ShowSaveFileAsync("Save Theme", "MyTheme.theme.json", ThemeFileFilter);
        if (string.IsNullOrWhiteSpace(path)) return;
        try
        {
            ThemeProfileIo.Save(path, _themeService.GetEffectiveCustomColors());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Export theme failed");
            await _dialogs.ShowAlertAsync("Export failed", ex.Message);
        }
    }

    private async Task ImportThemeAsync()
    {
        var path = await _dialogs.ShowOpenFileAsync("Load Theme", ThemeFileFilter);
        if (string.IsNullOrWhiteSpace(path)) return;

        var profile = ThemeProfileIo.Load(path);
        if (profile is null)
        {
            await _dialogs.ShowAlertAsync("Import failed",
                "That file isn't a valid theme file, so no colors were changed.");
            return;
        }
        _themeService.ImportCustomColors(profile.Colors);
    }

    // --- parse helpers ---

    private static Guid? ResolveId(PaletteArgValue value, Guid? fallback)
        => value.Tag is Guid g ? g : fallback;

    private static bool TryParsePriority(string s, out TaskPriority p)
    {
        p = TaskPriority.None;
        s = s.Trim();
        return s.Length > 0 && Enum.TryParse(s, ignoreCase: true, out p) && Enum.IsDefined(p);
    }

    private static bool TryParseProgress(string s, out int pct)
    {
        pct = 0;
        s = s.Trim().TrimEnd('%').Trim();
        if (!int.TryParse(s, out var n)) return false;
        pct = Math.Clamp(n, 0, 100);
        return true;
    }

    private static bool TryParseDate(string s, out DateTime d)
    {
        d = default;
        s = s.Trim();
        if (s.Length == 0) return false;
        switch (s.ToLowerInvariant())
        {
            case "today": d = DateTime.Today; return true;
            case "tomorrow": d = DateTime.Today.AddDays(1); return true;
        }
        return DateTime.TryParse(s, out d);
    }

    private static (double Width, double Height)? ResolvePageSize(string s) => s.Trim().ToLowerInvariant() switch
    {
        "" or "default" => (3000d, 2000d),
        "a4" or "a4 landscape" => (3508d, 2480d),
        "a4 portrait" => (2480d, 3508d),
        "letter" => (3300d, 2550d),
        "large" => (5000d, 3500d),
        _ => null,
    };
}
