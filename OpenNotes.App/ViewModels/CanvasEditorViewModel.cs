using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using OpenNotes.Export;
using OpenNotes.Interfaces;
using OpenNotes.Models;
using OpenNotes.Models.Blocks;
using OpenNotes.Persistence;
using OpenNotes.UndoRedo.Canvas;
using OpenNotes.ViewModels.Canvas;

namespace OpenNotes.ViewModels;

public enum CanvasTool { Select, Rectangle, Ellipse, Diamond, Text, Connector, NewTask, Pen, Marker, Highlighter, InkEraser }

/// <summary>Free-form, Visio-like canvas editor. Backed by a multi-page <see cref="CanvasDocument"/>
/// (<c>.taskcanvas</c> archive) that is either task-owned or a standalone workspace canvas. Only the
/// active page's nodes/connectors are materialized as ViewModels — inactive pages exist as data plus
/// a static thumbnail, so page count doesn't multiply live visual-tree cost.</summary>
public partial class CanvasEditorViewModel : ViewModelBase
{
    private readonly IWorkspaceService _workspaceService;
    private readonly ICanvasDocumentService _documentService;
    private readonly ITaskService _taskService;
    private readonly INavigationService _navigation;
    private readonly IUndoRedoService _undoRedo;
    private readonly IDialogService _dialogs;
    private readonly IMermaidSvgExporter _mermaidExporter;
    private readonly ILatexPngRenderer _latexRenderer;
    private readonly ICanvasThemeService _canvasTheme;
    private readonly ICanvasPdfExporter _pdfExporter;
    private readonly IThemeService _themeService;
    private readonly WorkspaceRepository _workspaceRepository;
    private readonly ILogger<CanvasEditorViewModel> _logger;

    private CanvasDocument _document = new();
    private DiagramModel _diagram = new(); // the ACTIVE page's payload; reassigned on page switch
    private Guid _workspaceId;
    private Guid _taskId; // Guid.Empty for standalone documents
    private int _insertOffset; // cascading placement offset for "Insert Block"
    private bool _suppressPageSwitch; // guards SelectedPage churn during document (re)load

    /// <summary>Set by the view: captures a PNG thumbnail of the currently rendered canvas.
    /// Null (or a null return) when the view isn't loaded — thumbnails are then simply skipped.</summary>
    public Func<byte[]?>? ThumbnailProvider { get; set; }

    [ObservableProperty] private bool _showGrid = true;
    [ObservableProperty] private bool _snapToGrid = true;
    [ObservableProperty] private double _gridSize = 20;
    [ObservableProperty] private double _zoom = 1.0;
    [ObservableProperty] private double _canvasWidth = 3000;
    [ObservableProperty] private double _canvasHeight = 2000;
    [ObservableProperty] private CanvasTool _activeTool = CanvasTool.Select;
    [ObservableProperty] private CanvasNodeViewModel? _selectedNode;
    [ObservableProperty] private CanvasConnectorViewModel? _selectedConnector;
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private bool _isPlacingAsync; // guards re-entrant async placement
    [ObservableProperty] private CanvasPageViewModel? _selectedPage;
    [ObservableProperty] private bool _isOverviewVisible;
    [ObservableProperty] private bool _isStandalone;

    /// <summary>The active page's floating (page-level) ink. The overlay InkCanvas binds directly to
    /// this instance; it is swapped out on page switch and serialized to
    /// <see cref="CanvasPage.FloatingInk"/> on deactivate/save.</summary>
    [ObservableProperty] private StrokeCollection _floatingStrokes = [];

    /// <summary>Current ink color (hex) shared by pen/marker/highlighter.</summary>
    [ObservableProperty] private string _inkColor = "#222222";

    /// <summary>How the overlay InkCanvas behaves for the current tool: drawing for the three ink
    /// tools, stroke-erase for the eraser, and None (plus hit-test transparent, so nodes stay
    /// clickable underneath) for everything else.</summary>
    public InkCanvasEditingMode InkEditingMode => ActiveTool switch
    {
        CanvasTool.Pen or CanvasTool.Marker or CanvasTool.Highlighter => InkCanvasEditingMode.Ink,
        CanvasTool.InkEraser => InkCanvasEditingMode.EraseByStroke,
        _ => InkCanvasEditingMode.None,
    };

    public bool IsInkToolActive => InkEditingMode != InkCanvasEditingMode.None;

    /// <summary>Brush tip for the current ink tool/color. Rebuilt (new instance) whenever either
    /// changes so the InkCanvas binding refreshes.</summary>
    public DrawingAttributes InkAttributes
    {
        get
        {
            Color color;
            try { color = (Color)ColorConverter.ConvertFromString(InkColor); }
            catch { color = Colors.Black; }

            return ActiveTool switch
            {
                CanvasTool.Marker => new DrawingAttributes { Color = color, Width = 6, Height = 6, FitToCurve = true },
                CanvasTool.Highlighter => new DrawingAttributes
                {
                    Color = color,
                    IsHighlighter = true,
                    StylusTip = StylusTip.Rectangle,
                    Width = 8,
                    Height = 22,
                },
                _ => new DrawingAttributes { Color = color, Width = 2.2, Height = 2.2, FitToCurve = true },
            };
        }
    }

    partial void OnActiveToolChanged(CanvasTool value)
    {
        OnPropertyChanged(nameof(InkEditingMode));
        OnPropertyChanged(nameof(IsInkToolActive));
        OnPropertyChanged(nameof(InkAttributes));
    }

    partial void OnInkColorChanged(string value) => OnPropertyChanged(nameof(InkAttributes));

    [RelayCommand]
    private void SetInkColor(string? color)
    {
        if (!string.IsNullOrWhiteSpace(color)) InkColor = color;
    }

    public ObservableCollection<CanvasNodeViewModel> Nodes { get; } = [];
    public ObservableCollection<CanvasConnectorViewModel> Connectors { get; } = [];

    /// <summary>Ordered page tabs (metadata + thumbnail only; see class remarks).</summary>
    public ObservableCollection<CanvasPageViewModel> Pages { get; } = [];

    /// <summary>Content blocks of the current task, available to snapshot onto the canvas.</summary>
    public ObservableCollection<ContentBlock> AvailableBlocks { get; } = [];

    public CanvasEditorViewModel(
        IWorkspaceService workspaceService,
        ICanvasDocumentService documentService,
        ITaskService taskService,
        INavigationService navigation,
        IUndoRedoService undoRedo,
        IDialogService dialogs,
        IMermaidSvgExporter mermaidExporter,
        ILatexPngRenderer latexRenderer,
        ICanvasThemeService canvasTheme,
        ICanvasPdfExporter pdfExporter,
        IThemeService themeService,
        WorkspaceRepository workspaceRepository,
        ILogger<CanvasEditorViewModel> logger)
    {
        _workspaceService = workspaceService;
        _documentService = documentService;
        _taskService = taskService;
        _navigation = navigation;
        _undoRedo = undoRedo;
        _dialogs = dialogs;
        _mermaidExporter = mermaidExporter;
        _latexRenderer = latexRenderer;
        _canvasTheme = canvasTheme;
        _pdfExporter = pdfExporter;
        _themeService = themeService;
        _workspaceRepository = workspaceRepository;
        _logger = logger;
        Title = "Canvas";

        // Rendered mermaid PNGs and code SVGs bake theme colors in — re-render them when the app
        // theme changes while this canvas is open. Unsubscribed in OnViewUnloaded (transient VM;
        // the singleton ThemeService must not pin dead editors).
        _themeService.ThemeChanged += OnAppThemeChanged;
    }

    private async void OnAppThemeChanged(object? sender, string themeName)
    {
        if (IsPlacingAsync) return;
        IsPlacingAsync = true; // block re-entrant placement/editing during the batch re-render
        try
        {
            await RerenderMermaidNodesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to re-render canvas nodes after app theme change to {Theme}", themeName);
        }
        finally
        {
            IsPlacingAsync = false;
        }
    }

    /// <summary>Open a task's canvas document (created — or silently migrated from the legacy
    /// single-page format — on first use).</summary>
    public async Task LoadAsync(Guid workspaceId, Guid taskId, CancellationToken ct = default)
    {
        IsBusy = true;
        try
        {
            _workspaceId = workspaceId;
            _taskId = taskId;
            IsStandalone = false;

            var task = await _taskService.GetTaskAsync(workspaceId, taskId, ct);
            if (task is null) return;

            Title = $"Canvas: {task.Title}";
            _document = await _documentService.GetOrCreateForTaskAsync(workspaceId, task, ct);

            AvailableBlocks.Clear();
            foreach (var block in task.ContentBlocks.OrderBy(b => b.Order))
                AvailableBlocks.Add(block);

            LoadDocumentIntoView();
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Open a standalone (not task-owned) canvas document.</summary>
    public async Task LoadStandaloneAsync(Guid workspaceId, Guid documentId, CancellationToken ct = default)
    {
        IsBusy = true;
        try
        {
            _workspaceId = workspaceId;
            _taskId = Guid.Empty;
            IsStandalone = true;

            var doc = await _documentService.LoadAsync(workspaceId, documentId, ct);
            if (doc is null)
            {
                _logger.LogError("Canvas document {DocumentId} could not be loaded", documentId);
                return;
            }

            _document = doc;
            Title = doc.Manifest.Title;
            AvailableBlocks.Clear(); // no owning task → nothing to insert or commit blocks to

            LoadDocumentIntoView();
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>(Re)build the page tab list from <see cref="_document"/> and activate the first page.</summary>
    private void LoadDocumentIntoView()
    {
        _canvasTheme.Apply(_document.Manifest.ThemeColors);
        _insertOffset = 0;
        _suppressPageSwitch = true;
        try
        {
            Pages.Clear();
            foreach (var page in _document.Pages)
                Pages.Add(new CanvasPageViewModel(page));
            SelectedPage = Pages.FirstOrDefault();
        }
        finally
        {
            _suppressPageSwitch = false;
        }

        if (SelectedPage is not null)
            ActivatePage(SelectedPage);
        IsDirty = false;

        // Legacy Latex nodes (saved in the WpfMath FormulaControl era) have no rendered PNG —
        // self-heal them lazily so they show real KaTeX output instead of the raw-source fallback.
        _ = SelfHealLatexNodesAsync();
    }

    /// <summary>Render a KaTeX PNG for every Latex node that doesn't have one yet (all pages).
    /// Best-effort: a failure just leaves the raw-source fallback visible.</summary>
    private async Task SelfHealLatexNodesAsync()
    {
        try
        {
            static bool NeedsPng(DiagramNode n) =>
                IsLatexNode(n) && (string.IsNullOrEmpty(n.ImagePath) || !File.Exists(n.ImagePath));

            foreach (var vm in Nodes.Where(n => NeedsPng(n.Node)).ToList())
            {
                var path = await RenderLatexPngPathAsync(vm.Node.LatexContent!);
                if (path is not null) vm.ImagePath = path;
            }
            foreach (var page in _document.Pages.Where(p => p != SelectedPage?.Page))
            {
                foreach (var node in page.Diagram.Nodes.Where(NeedsPng))
                {
                    var path = await RenderLatexPngPathAsync(node.LatexContent!);
                    if (path is not null) node.ImagePath = path;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LaTeX node self-heal render failed.");
        }
    }

    partial void OnSelectedPageChanged(CanvasPageViewModel? oldValue, CanvasPageViewModel? newValue)
    {
        if (_suppressPageSwitch || newValue is null || newValue == oldValue) return;

        if (oldValue is not null)
            DeactivatePage(oldValue);
        ActivatePage(newValue);
    }

    /// <summary>Park the outgoing page: flush view state into its model and cache a thumbnail.
    /// Its node data already lives in its DiagramModel (all edits mutate the model directly),
    /// so nothing else needs copying.</summary>
    private void DeactivatePage(CanvasPageViewModel page)
    {
        page.IsActive = false;
        SyncViewState();
        page.Page.FloatingInk = InkSerializer.ToBytes(FloatingStrokes);
        CaptureThumbnail(page);
    }

    /// <summary>Materialize a page's nodes/connectors as live ViewModels — only ever done for the
    /// single active page (lazy loading: complex node renderers exist only while their page is
    /// visible). Clears the undo stack: undo entries hold references into the previous page.</summary>
    private void ActivatePage(CanvasPageViewModel page)
    {
        _diagram = page.Page.Diagram;
        page.IsActive = true;

        ShowGrid = _diagram.ShowGrid;
        SnapToGrid = _diagram.SnapToGrid;
        GridSize = _diagram.GridSize > 0 ? _diagram.GridSize : 20;
        Zoom = _diagram.ZoomLevel > 0 ? _diagram.ZoomLevel : 1.0;
        CanvasWidth = _diagram.CanvasWidth > 0 ? _diagram.CanvasWidth : 3000;
        CanvasHeight = _diagram.CanvasHeight > 0 ? _diagram.CanvasHeight : 2000;

        FloatingStrokes.StrokesChanged -= OnFloatingStrokesChanged;
        var floating = InkSerializer.FromBytes(page.Page.FloatingInk);
        floating.StrokesChanged += OnFloatingStrokesChanged;
        FloatingStrokes = floating;

        SelectNode(null);
        SelectConnector(null);
        Nodes.Clear();
        foreach (var connector in Connectors) connector.Detach();
        Connectors.Clear();

        var byId = new Dictionary<Guid, CanvasNodeViewModel>();
        foreach (var node in _diagram.Nodes)
        {
            var vm = new CanvasNodeViewModel(node) { Owner = this };
            vm.Changed += OnNodeChangedDirty;
            Nodes.Add(vm);
            byId[node.Id] = vm;
        }

        foreach (var c in _diagram.Connectors)
        {
            if (byId.TryGetValue(c.SourceNodeId, out var s) && byId.TryGetValue(c.TargetNodeId, out var t))
                Connectors.Add(new CanvasConnectorViewModel(c, s, t) { Owner = this });
        }

        _undoRedo.Clear();
    }

    private void OnFloatingStrokesChanged(object? sender, StrokeCollectionChangedEventArgs e) => IsDirty = true;

    private void CaptureThumbnail(CanvasPageViewModel page)
    {
        try
        {
            var png = ThumbnailProvider?.Invoke();
            if (png is { Length: > 0 })
            {
                page.SetThumbnail(png);
                IsDirty = true; // the cached thumbnail is part of the persisted document
            }
            else
            {
                page.RefreshThumbnail(); // at least refresh the node count shown in the overview
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Thumbnail capture failed for canvas page {PageId}", page.Id);
        }
    }

    // ----- page management -----

    [RelayCommand]
    private void AddPage()
    {
        var page = _document.AddPage();
        var vm = new CanvasPageViewModel(page);
        Pages.Add(vm);
        IsDirty = true;
        SelectedPage = vm; // triggers the normal deactivate/activate switch
    }

    [RelayCommand]
    private async Task RenamePage(CanvasPageViewModel? page)
    {
        if (page is null) return;
        var name = await _dialogs.ShowInputAsync("Rename Page", "Page name:", page.Title);
        if (string.IsNullOrWhiteSpace(name) || name == page.Title) return;
        page.Title = name;
        IsDirty = true;
    }

    [RelayCommand]
    private async Task DeletePage(CanvasPageViewModel? page)
    {
        if (page is null) return;
        if (Pages.Count <= 1)
        {
            await _dialogs.ShowAlertAsync("Cannot delete page", "A canvas needs at least one page.");
            return;
        }

        var hasContent = page.Page.Diagram.Nodes.Count > 0 || page.Page.FloatingInk is { Length: > 0 };
        if (hasContent)
        {
            var confirm = await _dialogs.ShowConfirmAsync(
                "Delete Page",
                $"Delete page '{page.Title}' and everything on it? This cannot be undone.",
                "Delete", "Cancel");
            if (!confirm) return;
        }

        var index = Pages.IndexOf(page);
        _document.RemovePage(page.Id);
        Pages.Remove(page);
        IsDirty = true;

        if (SelectedPage == page || SelectedPage is null)
            SelectedPage = Pages[Math.Clamp(index, 0, Pages.Count - 1)];
    }

    [RelayCommand]
    private void MovePageLeft(CanvasPageViewModel? page) => MovePage(page, -1);

    [RelayCommand]
    private void MovePageRight(CanvasPageViewModel? page) => MovePage(page, +1);

    private void MovePage(CanvasPageViewModel? page, int delta)
    {
        if (page is null) return;
        var from = Pages.IndexOf(page);
        var to = from + delta;
        if (from < 0 || to < 0 || to >= Pages.Count) return;

        Pages.Move(from, to);
        var docFrom = _document.Pages.IndexOf(page.Page);
        _document.Pages.RemoveAt(docFrom);
        _document.Pages.Insert(docFrom + delta, page.Page);
        IsDirty = true;
    }

    [RelayCommand]
    private void ToggleOverview()
    {
        // Entering the overview refreshes the active page's thumbnail so the grid never shows
        // a stale (or missing) snapshot of the page the user was just editing.
        if (!IsOverviewVisible && SelectedPage is not null)
            CaptureThumbnail(SelectedPage);
        IsOverviewVisible = !IsOverviewVisible;
    }

    /// <summary>Overview grid click: jump to that page and close the overview.</summary>
    [RelayCommand]
    private void ActivatePageFromOverview(CanvasPageViewModel? page)
    {
        if (page is null) return;
        SelectedPage = page;
        IsOverviewVisible = false;
    }

    public double Snap(double value) => SnapToGrid ? Math.Round(value / GridSize) * GridSize : value;

    [RelayCommand]
    private void SetTool(string tool)
    {
        if (Enum.TryParse<CanvasTool>(tool, out var t)) ActiveTool = t;
    }

    /// <summary>Minimum node edge for placed/drag-created shapes, canvas px.</summary>
    private const double MinPlacedSize = 24;

    /// <summary>Place a new shape/text node for the ACTIVE tool at the (already canvas-space)
    /// point at its default size. Undoable; one-shot (the tool reverts to Select).</summary>
    public void PlaceNode(double x, double y)
    {
        if (ActiveTool is CanvasTool.Select or CanvasTool.Connector) return;
        PlaceNodeAt(ActiveToolShape, x, y);
        ActiveTool = CanvasTool.Select; // one-shot placement
    }

    /// <summary>Drag-to-size placement: the shape fills the dragged rectangle (snapped, min-clamped).
    /// Undoable; one-shot like <see cref="PlaceNode"/>.</summary>
    public void PlaceNodeSized(double x, double y, double width, double height)
    {
        if (ActiveTool is CanvasTool.Select or CanvasTool.Connector) return;
        PlaceNodeAt(ActiveToolShape, x, y, Snap(width), Snap(height));
        ActiveTool = CanvasTool.Select;
    }

    private NodeShape ActiveToolShape => ActiveTool switch
    {
        CanvasTool.Ellipse => NodeShape.Ellipse,
        CanvasTool.Diamond => NodeShape.Diamond,
        CanvasTool.Text => NodeShape.Text,
        _ => NodeShape.Rectangle,
    };

    /// <summary>Place a new shape/text node at the (already canvas-space) point. Width/height
    /// default to the shape's standard size. Undoable.</summary>
    public void PlaceNodeAt(NodeShape shape, double x, double y, double? width = null, double? height = null)
    {
        var isText = shape == NodeShape.Text;
        var node = new DiagramNode
        {
            LayerId = _diagram.Layers.FirstOrDefault()?.Id ?? Guid.Empty,
            Shape = shape,
            X = Snap(x),
            Y = Snap(y),
            Width = Math.Max(MinPlacedSize, width ?? 120),
            Height = Math.Max(MinPlacedSize, height ?? (isText ? 32 : 60)),
            Label = isText ? "Text" : string.Empty,
        };

        var vm = new CanvasNodeViewModel(node) { Owner = this };
        _undoRedo.Push(new AddNodeCommand(this, vm));
        SelectNode(vm);
    }

    // ----- empty-canvas context menu (Select tool only): creators land at the right-clicked
    // point, captured by the view in ContextMenuOpening. -----

    private double _contextX = 40, _contextY = 40;

    /// <summary>Remember the canvas-space point of the surface right-click so the context menu's
    /// "… here" commands create at that spot.</summary>
    public void SetContextPoint(double x, double y) { _contextX = x; _contextY = y; }

    [RelayCommand]
    private void PlaceShapeAtContextPoint(string? shape)
    {
        if (Enum.TryParse<NodeShape>(shape, out var s)) PlaceNodeAt(s, _contextX, _contextY);
    }

    [RelayCommand]
    private Task NewTaskAtContextPointAsync() => PlaceNewTaskNodeAsync(_contextX, _contextY);

    [RelayCommand]
    private Task InsertBlockAtContextPointAsync(ContentBlock? block) =>
        InsertBlockAtAsync(block, _contextX, _contextY);

    [RelayCommand]
    private Task CreateBlockNodeAtContextPointAsync(string? kind) =>
        CreateBlockNodeAtAsync(kind, _contextX, _contextY);

    /// <summary>Snapshot one of the task's content blocks onto the canvas as a node. One-time copy —
    /// the node does not stay in sync with the source block. Placed at the given canvas point, or
    /// at the cascading default origin when none is supplied (toolbar path). Undoable.</summary>
    [RelayCommand]
    private async Task InsertBlockAsync(ContentBlock? block) => await InsertBlockAtAsync(block, null, null);

    private async Task InsertBlockAtAsync(ContentBlock? block, double? atX, double? atY)
    {
        if (block is null || IsPlacingAsync) return;
        IsPlacingAsync = true;
        try
        {
            var (x, y) = ResolveInsertPoint(atX, atY);

            var layerId = _diagram.Layers.FirstOrDefault()?.Id ?? Guid.Empty;
            var node = await BlockToNodeMapper.ToNodeAsync(block, x, y, layerId, _mermaidExporter,
                SaveMermaidPngAsync, MermaidThemeVariables, BuildCodePalette());

            // LaTeX snapshots render to a KaTeX PNG here in the VM (the mapper stays WebView2-free
            // for LaTeX); a failed render leaves the raw-source fallback visible in the node.
            if (IsLatexNode(node))
                node.ImagePath = await RenderLatexPngPathAsync(node.LatexContent!) ?? node.ImagePath;

            var vm = new CanvasNodeViewModel(node) { Owner = this };
            _undoRedo.Push(new AddNodeCommand(this, vm));
            SelectNode(vm);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to insert block onto the canvas.");
        }
        finally
        {
            IsPlacingAsync = false;
        }
    }

    /// <summary>Create a brand-new task (appended to the task list via the normal creation pipeline)
    /// and drop a link node for it on the canvas at the clicked point. Undoable (node only).</summary>
    public async Task PlaceNewTaskNodeAsync(double x, double y)
    {
        if (_workspaceId == Guid.Empty || IsPlacingAsync) return;
        IsPlacingAsync = true;
        try
        {
            var title = await _dialogs.ShowInputAsync("New Task", "Task title:", "Untitled Task");
            if (string.IsNullOrWhiteSpace(title)) return;

            var task = await _taskService.CreateTaskAsync(_workspaceId, title);

            var node = new DiagramNode
            {
                LayerId = _diagram.Layers.FirstOrDefault()?.Id ?? Guid.Empty,
                Shape = NodeShape.TaskLink,
                Label = title,
                LinkedTaskId = task.Id,
                X = Snap(x),
                Y = Snap(y),
                Width = 180,
                Height = 60,
                FillColor = "#FFF4CE",
            };

            var vm = new CanvasNodeViewModel(node) { Owner = this };
            _undoRedo.Push(new AddNodeCommand(this, vm));
            SelectNode(vm);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create a task from the canvas.");
        }
        finally
        {
            IsPlacingAsync = false;
            ActiveTool = CanvasTool.Select; // one-shot placement
        }
    }

    /// <summary>Open the task editor for a task-link node's underlying task.</summary>
    [RelayCommand]
    private async Task OpenLinkedTask(CanvasNodeViewModel? node)
    {
        if (node?.LinkedTaskId is not Guid taskId) return;
        if (IsDirty) await SaveAsync();
        var wsId = _workspaceId;
        _navigation.NavigateTo<TaskEditorViewModel>(vm => _ = vm.LoadTaskAsync(wsId, taskId));
    }

    /// <summary>The cascading default insert origin, or the caller-supplied point (context menu).
    /// The cascade stops successive toolbar inserts stacking exactly on top of each other.</summary>
    private (double X, double Y) ResolveInsertPoint(double? atX, double? atY)
    {
        if (atX is not null && atY is not null) return (Snap(atX.Value), Snap(atY.Value));
        double p = Snap(40 + _insertOffset * 24);
        _insertOffset = (_insertOffset + 1) % 10;
        return (p, p);
    }

    /// <summary>Create a brand-new, block-backed content node on the canvas (not yet added to the task).
    /// Types that can't be edited inline (LaTeX, Code, Mermaid, Image) immediately open their editor.</summary>
    [RelayCommand]
    private async Task CreateBlockNode(string? kind) => await CreateBlockNodeAtAsync(kind, null, null);

    private async Task CreateBlockNodeAtAsync(string? kind, double? atX, double? atY)
    {
        if (string.IsNullOrEmpty(kind)) return;

        var (shape, w, h) = kind switch
        {
            "text" => (NodeShape.Text, 200.0, 60.0),
            "markdown" => (NodeShape.StickyNote, 240.0, 140.0),
            "latex" => (NodeShape.Latex, 200.0, 80.0),
            "image" => (NodeShape.Image, 240.0, 180.0),
            "checklist" => (NodeShape.Checklist, 220.0, 120.0),
            "code" => (NodeShape.Svg, 320.0, 200.0),
            "mermaid" => (NodeShape.Svg, 280.0, 220.0),
            _ => (NodeShape.Text, 200.0, 60.0),
        };

        var (x, y) = ResolveInsertPoint(atX, atY);

        var label = kind switch
        {
            "text" => "Text",
            "markdown" => "Markdown",
            "checklist" => "☐ New item",
            _ => string.Empty,
        };

        var node = new DiagramNode
        {
            LayerId = _diagram.Layers.FirstOrDefault()?.Id ?? Guid.Empty,
            Shape = shape,
            BlockKind = kind,
            X = x, Y = y, Width = w, Height = h,
            Label = label,
            AuthoredSource = label,
            AuthoredLanguage = kind == "code" ? "plaintext" : null,
        };

        var vm = new CanvasNodeViewModel(node) { Owner = this };
        _undoRedo.Push(new AddNodeCommand(this, vm));
        SelectNode(vm);

        if (kind is "latex" or "code" or "mermaid" or "image")
            await EditNodeContent(vm);
    }

    /// <summary>The document's effective colors as Mermaid themeVariables: overrides win, missing
    /// keys fall back to the app theme's canvas defaults so renders always track the current theme.</summary>
    private IReadOnlyDictionary<string, string>? MermaidThemeVariables =>
        _canvasTheme.GetMermaidThemeVariables(_document.Manifest.ThemeColors);

    /// <summary>Save a captured Mermaid PNG into the workspace's attachments folder and return its path,
    /// so the node can render it via the same <see cref="NodeShape.Image"/>/<c>ImagePath</c> pipeline
    /// already used for user-uploaded images (no separate raster-storage plumbing needed).</summary>
    private async Task<string> SaveMermaidPngAsync(byte[] png) => await SaveRenderedPngAsync(png, "mermaid");

    private async Task<string> SaveRenderedPngAsync(byte[] png, string prefix)
    {
        var folder = _workspaceRepository.GetAttachmentsFolder(_workspaceId);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"{prefix}-{Guid.NewGuid()}.png");
        await File.WriteAllBytesAsync(path, png);
        return path;
    }

    /// <summary>The effective canvas text color (document override else theme default) as hex —
    /// the color KaTeX bakes into LaTeX node PNGs, matching FormulaControl-era theming.</summary>
    private static string CurrentCanvasTextHex() =>
        System.Windows.Application.Current?.TryFindResource("CanvasTextBrush")
            is System.Windows.Media.SolidColorBrush b
            ? $"#{b.Color.R:X2}{b.Color.G:X2}{b.Color.B:X2}"
            : "#E0E0E0";

    /// <summary>Render a LaTeX node's formula to a KaTeX PNG and return its saved path, or null
    /// when rendering is unavailable (the node then shows its raw-source fallback).</summary>
    private async Task<string?> RenderLatexPngPathAsync(string formula)
    {
        var png = await _latexRenderer.RenderToPngAsync(formula, CurrentCanvasTextHex());
        return png is null ? null : await SaveRenderedPngAsync(png, "latex");
    }

    /// <summary>Edit a block-backed node's content with a type-appropriate editor; re-renders Code/Mermaid.</summary>
    [RelayCommand]
    private async Task EditNodeContent(CanvasNodeViewModel? node)
    {
        if (node is null || IsPlacingAsync) return;
        IsPlacingAsync = true;
        try
        {
            switch (node.BlockKind)
            {
                case "text":
                case "markdown":
                {
                    var r = await _dialogs.ShowMultilineInputAsync("Edit content", "Content:", node.AuthoredSource ?? node.Label ?? "");
                    if (r is null) return;
                    node.Label = r; node.AuthoredSource = r;
                    break;
                }
                case "checklist":
                {
                    var r = await _dialogs.ShowMultilineInputAsync("Edit checklist", "One item per line (prefix ☑ for done):", node.AuthoredSource ?? node.Label ?? "");
                    if (r is null) return;
                    node.Label = r; node.AuthoredSource = r;
                    break;
                }
                case "latex":
                {
                    var r = await _dialogs.ShowMultilineInputAsync("Edit LaTeX", "Formula (full KaTeX support; $/$$ delimiters optional):", node.AuthoredSource ?? node.LatexContent ?? "");
                    if (r is null) return;
                    node.AuthoredSource = r; node.LatexContent = r;
                    var latexPng = await RenderLatexPngPathAsync(r);
                    if (latexPng is not null) node.ImagePath = latexPng;
                    break;
                }
                case "image":
                {
                    var p = await _dialogs.ShowOpenFileAsync("Select image", "Images|*.png;*.jpg;*.jpeg;*.gif;*.bmp|All Files|*.*");
                    if (p is null) return;
                    node.ImagePath = p; node.AuthoredSource = p;
                    break;
                }
                case "code":
                {
                    var r = await _dialogs.ShowCodeInputAsync("Edit code",
                        node.AuthoredSource ?? "", node.AuthoredLanguage ?? "plaintext",
                        Blocks.CodeBlockViewModel.SupportedLanguages);
                    if (r is null) return;
                    node.AuthoredSource = r.Value.Code;
                    node.AuthoredLanguage = r.Value.Language;
                    node.SvgContent = CodeToSvgRenderer.Render(
                        r.Value.Code, r.Value.Language, node.Node.AuthoredShowLineNumbers, BuildCodePalette());
                    break;
                }
                case "mermaid":
                {
                    var r = await _dialogs.ShowMultilineInputAsync("Edit Mermaid", "Definition:", node.AuthoredSource ?? "");
                    if (r is null) return;
                    node.AuthoredSource = r;
                    var png = await _mermaidExporter.RenderToPngAsync(r, MermaidThemeVariables);
                    if (png is null)
                    {
                        // Text shape, not Svg: Svg-shaped nodes suppress the inline label,
                        // which would leave the failure completely invisible on the canvas.
                        node.Shape = NodeShape.Text;
                        node.Label = "Mermaid (render failed)";
                    }
                    else
                    {
                        node.Shape = NodeShape.Image;
                        node.ImagePath = await SaveMermaidPngAsync(png);
                        node.Label = string.Empty;
                    }
                    break;
                }
                default:
                    return;
            }
            IsDirty = true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to edit canvas node content.");
        }
        finally
        {
            IsPlacingAsync = false;
        }
    }

    /// <summary>Push a block-backed node into the parent task's content blocks: updates the linked block in
    /// place if the node came from one, otherwise appends a new block. Fires TaskUpdated so the task list /
    /// dashboard refresh; the editor shows it on return.</summary>
    [RelayCommand]
    private async Task CommitNodeToTask(CanvasNodeViewModel? node)
    {
        if (node is null || _workspaceId == Guid.Empty || IsPlacingAsync) return;
        if (_taskId == Guid.Empty)
        {
            await _dialogs.ShowAlertAsync("No task", "This is a standalone canvas — it has no owning task to add blocks to.");
            return;
        }
        if (!NodeToBlockMapper.CanConvert(node))
        {
            await _dialogs.ShowAlertAsync("Not a content block",
                "This node isn't a content block, so it can't be added to the task. Use a block created via \"New Block\" or an inserted block.");
            return;
        }

        IsPlacingAsync = true;
        try
        {
            var block = NodeToBlockMapper.ToBlock(node);
            var task = await _taskService.GetTaskAsync(_workspaceId, _taskId);
            if (task is null) return;

            int idx = node.SourceContentBlockId is Guid sid ? task.ContentBlocks.FindIndex(b => b.Id == sid) : -1;
            if (idx >= 0)
            {
                block.Order = task.ContentBlocks[idx].Order;
                task.ContentBlocks[idx] = block;
            }
            else
            {
                block.Order = task.ContentBlocks.Count;
                task.ContentBlocks.Add(block);
                node.SourceContentBlockId = block.Id; // link so a re-push updates in place
            }

            await _taskService.UpdateTaskAsync(_workspaceId, task);
            IsDirty = true;
            await SaveAsync(); // persist the node's new SourceContentBlockId link
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to commit canvas node to the task.");
        }
        finally
        {
            IsPlacingAsync = false;
        }
    }

    /// <summary>A stroke was just drawn on the overlay InkCanvas (which has already added it to
    /// <see cref="FloatingStrokes"/>). If it lies fully within a node's bounds it is re-parented
    /// into that node's bound ink — translated to node-local coordinates so it moves with the
    /// node — otherwise it stays floating. Either way the add is undoable.</summary>
    public void HandleStrokeCollected(Stroke stroke)
    {
        var bounds = stroke.GetBounds();
        // Topmost wins: nodes render in list order, so search from the end.
        var target = Nodes.LastOrDefault(n => new Rect(n.X, n.Y, n.Width, n.Height).Contains(bounds));

        if (target is not null)
        {
            FloatingStrokes.Remove(stroke);
            var toLocal = Matrix.Identity;
            toLocal.Translate(-target.X, -target.Y);
            stroke.Transform(toLocal, applyToStylusTip: false);
            _undoRedo.Push(new AddNodeInkStrokeCommand(target, stroke));
        }
        else
        {
            _undoRedo.Push(new AddFloatingStrokeCommand(FloatingStrokes, stroke));
        }
        IsDirty = true;
    }

    /// <summary>Erase one floating-ink stroke (routed here from the InkCanvas's cancelled
    /// StrokeErasing event so the erase is undoable).</summary>
    public void EraseStroke(Stroke stroke) =>
        _undoRedo.Push(new EraseFloatingStrokeCommand(FloatingStrokes, stroke));

    /// <summary>Erase node-bound ink under the eraser point (canvas coords). The InkCanvas eraser
    /// only hit-tests its own floating strokes, so the view calls this while the ⌫ tool sweeps.
    /// One undo entry per removed stroke.</summary>
    public void EraseNodeInkAt(double x, double y)
    {
        const double eraserDiameter = 10;
        foreach (var node in Nodes)
        {
            if (x < node.X || x > node.X + node.Width || y < node.Y || y > node.Y + node.Height)
                continue;
            var local = new Point(x - node.X, y - node.Y);
            foreach (var hit in node.InkStrokes.HitTest(local, eraserDiameter).ToList())
                _undoRedo.Push(new EraseNodeInkStrokeCommand(node, hit));
        }
        // IsDirty follows from the node's StrokesChanged write-through when anything was removed.
    }

    /// <summary>Remove all ink bound to a node (context menu). Undoable.</summary>
    [RelayCommand]
    private void ClearNodeInk(CanvasNodeViewModel? node)
    {
        if (node is null || node.InkStrokes.Count == 0) return;
        _undoRedo.Push(new ClearNodeInkCommand(node));
    }

    /// <summary>Toggle the node's border chrome (context menu). Undoable.</summary>
    [RelayCommand]
    private void ToggleNodeBorder(CanvasNodeViewModel? node)
    {
        if (node is not null) _undoRedo.Push(new SetNodeBorderCommand(node, !node.ShowBorder));
    }

    // ----- z-order (context menu). Collection order is draw order; all four are undoable and
    // no-ops (no undo entry) when the node is already at the requested edge. -----

    [RelayCommand]
    private void BringNodeForward(CanvasNodeViewModel? node) => ReorderNode(node, +1);

    [RelayCommand]
    private void SendNodeBackward(CanvasNodeViewModel? node) => ReorderNode(node, -1);

    [RelayCommand]
    private void BringNodeToFront(CanvasNodeViewModel? node)
    {
        if (node is not null) ReorderNode(node, Nodes.Count - 1 - Nodes.IndexOf(node));
    }

    [RelayCommand]
    private void SendNodeToBack(CanvasNodeViewModel? node)
    {
        if (node is not null) ReorderNode(node, -Nodes.IndexOf(node));
    }

    private void ReorderNode(CanvasNodeViewModel? node, int delta)
    {
        if (node is null || delta == 0) return;
        int from = Nodes.IndexOf(node);
        if (from < 0) return;
        int to = Math.Clamp(from + delta, 0, Nodes.Count - 1);
        if (from == to) return;
        _undoRedo.Push(new ReorderNodeCommand(this, node, from, to));
    }

    /// <summary>Create an anchored connector between two nodes. Undoable.</summary>
    public void AddConnector(CanvasNodeViewModel from, CanvasNodeViewModel to)
    {
        if (from == to) return;
        var connector = new DiagramConnector { SourceNodeId = from.Id, TargetNodeId = to.Id };
        var vm = new CanvasConnectorViewModel(connector, from, to) { Owner = this };
        _undoRedo.Push(new AddConnectorCommand(this, vm));
    }

    public void SelectNode(CanvasNodeViewModel? node)
    {
        if (SelectedNode is not null) SelectedNode.IsSelected = false;
        SelectedNode = node;
        if (node is not null)
        {
            node.IsSelected = true;
            SelectConnector(null);
        }
    }

    public void SelectConnector(CanvasConnectorViewModel? connector)
    {
        if (SelectedConnector is not null) SelectedConnector.IsSelected = false;
        SelectedConnector = connector;
        if (connector is not null)
        {
            connector.IsSelected = true;
            SelectNode(null);
        }
    }

    /// <summary>Commit a completed drag as an undoable move (the live drag already moved the node).</summary>
    public void CommitMove(CanvasNodeViewModel node, double oldX, double oldY)
    {
        var newX = Snap(node.X);
        var newY = Snap(node.Y);
        node.X = newX;
        node.Y = newY;
        if (oldX == newX && oldY == newY) return;
        _undoRedo.Push(new MoveNodeCommand(node, oldX, oldY, newX, newY));
    }

    /// <summary>Commit a completed resize as undoable (the live drag already resized/moved the node).</summary>
    public void CommitResize(CanvasNodeViewModel node, double oldX, double oldY, double oldWidth, double oldHeight)
    {
        if (oldX == node.X && oldY == node.Y && oldWidth == node.Width && oldHeight == node.Height) return;
        _undoRedo.Push(new ResizeNodeCommand(node, oldX, oldY, oldWidth, oldHeight, node.X, node.Y, node.Width, node.Height));
    }

    [RelayCommand]
    private void DeleteSelected()
    {
        if (SelectedNode is not null) DeleteNode(SelectedNode);
        else if (SelectedConnector is not null) DeleteConnector(SelectedConnector);
    }

    /// <summary>Delete a specific node (and its attached connectors). Undoable.</summary>
    [RelayCommand]
    private void DeleteNode(CanvasNodeViewModel? node)
    {
        if (node is null) return;
        var attached = Connectors.Where(c => c.Source == node || c.Target == node).ToList();
        _undoRedo.Push(new DeleteNodeCommand(this, node, attached));
        if (SelectedNode == node) SelectedNode = null;
    }

    /// <summary>Delete a specific connector. Undoable.</summary>
    [RelayCommand]
    private void DeleteConnector(CanvasConnectorViewModel? connector)
    {
        if (connector is null) return;
        _undoRedo.Push(new DeleteConnectorCommand(this, connector));
        if (SelectedConnector == connector) SelectedConnector = null;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (_workspaceId == Guid.Empty) return;
        SyncViewState();
        if (SelectedPage is not null)
        {
            SelectedPage.Page.FloatingInk = InkSerializer.ToBytes(FloatingStrokes);
            CaptureThumbnail(SelectedPage);
        }
        await _documentService.SaveAsync(_workspaceId, _document, CancellationToken.None);
        IsDirty = false;
    }

    /// <summary>Back to where this canvas was opened from: the owning task's editor, or the
    /// canvas library for a standalone document.</summary>
    [RelayCommand]
    private async Task BackToEditor()
    {
        if (IsDirty) await SaveAsync();
        if (IsStandalone)
        {
            _navigation.NavigateTo<CanvasLibraryViewModel>();
            return;
        }
        var wsId = _workspaceId;
        var taskId = _taskId;
        _navigation.NavigateTo<TaskEditorViewModel>(vm => _ = vm.LoadTaskAsync(wsId, taskId));
    }

    partial void OnShowGridChanged(bool value) { _diagram.ShowGrid = value; IsDirty = true; }
    partial void OnSnapToGridChanged(bool value) { _diagram.SnapToGrid = value; IsDirty = true; }
    partial void OnZoomChanged(double value) => _diagram.ZoomLevel = value;
    partial void OnCanvasWidthChanged(double value) { _diagram.CanvasWidth = value; IsDirty = true; }
    partial void OnCanvasHeightChanged(double value) { _diagram.CanvasHeight = value; IsDirty = true; }

    /// <summary>Open the canvas page-size dialog and apply the chosen size. Shrinking below existing
    /// node bounds is allowed silently — nodes are left in place, matching how draw.io/Visio handle
    /// a page-size change (never move or delete content).</summary>
    [RelayCommand]
    private async Task ShowCanvasSizeDialog()
    {
        var result = await _dialogs.ShowCanvasSizeDialogAsync(CanvasWidth, CanvasHeight);
        if (result is not { } size) return;
        CanvasWidth = size.Width;
        CanvasHeight = size.Height;
    }

    /// <summary>Open the canvas color dialog; on commit persist the overrides in the document
    /// manifest, apply them as runtime resources (DynamicResource-bound text/LaTeX/checklist visuals
    /// re-brush instantly), and re-render every Mermaid node with the new hex values — Mermaid
    /// snapshots are static PNGs, so they can't pick the change up any other way.</summary>
    [RelayCommand]
    private async Task EditThemeAsync()
    {
        if (IsPlacingAsync) return;

        var defaults = new Dictionary<string, string>();
        foreach (var key in _canvasTheme.ThemeKeys)
        {
            if (_canvasTheme.GetThemeDefaultHex(key) is { } hex)
                defaults[key] = hex;
        }

        var result = await _dialogs.ShowCanvasThemeDialogAsync(_document.Manifest.ThemeColors, defaults);
        if (result is null) return;

        _document.Manifest.ThemeColors = result;
        _canvasTheme.Apply(result);
        IsDirty = true;

        IsPlacingAsync = true; // block re-entrant node placement/editing during the batch re-render
        try
        {
            await RerenderMermaidNodesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to re-render Mermaid nodes after a canvas theme change.");
        }
        finally
        {
            IsPlacingAsync = false;
        }
    }

    /// <summary>Re-render every previously rendered Mermaid and code node (all pages) with the
    /// document's current theme colors — both are static snapshots (PNG / SVG) that can't pick up a
    /// color change any other way. Active-page nodes go through their ViewModels (write-through +
    /// instant visual refresh); inactive pages' nodes are updated directly on the models and
    /// materialize with the new visuals on next activation.</summary>
    private async Task RerenderMermaidNodesAsync()
    {
        var vars = MermaidThemeVariables;
        var codePalette = BuildCodePalette();

        foreach (var vm in Nodes.Where(n => IsRenderedMermaid(n.Node)).ToList())
        {
            var png = await _mermaidExporter.RenderToPngAsync(vm.AuthoredSource!, vars);
            if (png is not null)
                vm.ImagePath = await SaveMermaidPngAsync(png);
        }
        foreach (var vm in Nodes.Where(n => IsRenderedCode(n.Node)).ToList())
            vm.SvgContent = CodeToSvgRenderer.Render(
                vm.AuthoredSource!, vm.Node.AuthoredLanguage ?? "plaintext", vm.Node.AuthoredShowLineNumbers, codePalette);

        foreach (var vm in Nodes.Where(n => IsLatexNode(n.Node)).ToList())
        {
            var path = await RenderLatexPngPathAsync(vm.Node.LatexContent!);
            if (path is not null) vm.ImagePath = path;
        }

        foreach (var page in _document.Pages.Where(p => p != SelectedPage?.Page))
        {
            foreach (var node in page.Diagram.Nodes.Where(IsRenderedMermaid))
            {
                var png = await _mermaidExporter.RenderToPngAsync(node.AuthoredSource!, vars);
                if (png is not null)
                    node.ImagePath = await SaveMermaidPngAsync(png);
            }
            foreach (var node in page.Diagram.Nodes.Where(IsRenderedCode))
                node.SvgContent = CodeToSvgRenderer.Render(
                    node.AuthoredSource!, node.AuthoredLanguage ?? "plaintext", node.AuthoredShowLineNumbers, codePalette);
            foreach (var node in page.Diagram.Nodes.Where(IsLatexNode))
            {
                var path = await RenderLatexPngPathAsync(node.LatexContent!);
                if (path is not null) node.ImagePath = path;
            }
        }
    }

    /// <summary>A LaTeX node with authored content — its PNG bakes in the canvas text color, so
    /// it re-renders on theme/color changes (and self-heals legacy WpfMath-era nodes that have no
    /// PNG at all).</summary>
    private static bool IsLatexNode(DiagramNode node) =>
        node.Shape == NodeShape.Latex && !string.IsNullOrWhiteSpace(node.LatexContent);

    /// <summary>A code node with a rendered SVG snapshot to re-color.</summary>
    private static bool IsRenderedCode(DiagramNode node) =>
        node.BlockKind == "code" && !string.IsNullOrEmpty(node.SvgContent) &&
        !string.IsNullOrWhiteSpace(node.AuthoredSource);

    /// <summary>Card colors for canvas code-SVG snapshots: the document's effective canvas
    /// surface/text colors, with a muted tone blended between them for the line-number gutter.</summary>
    private CodeSvgPalette BuildCodePalette() => CodeSvgPalette.FromPdfTheme(BuildPdfTheme());

    /// <summary>A Mermaid node that rendered successfully before (failed ones show a text
    /// placeholder until re-edited, so there's nothing to re-color).</summary>
    private static bool IsRenderedMermaid(DiagramNode node) =>
        node.BlockKind == "mermaid" && node.Shape == NodeShape.Image &&
        !string.IsNullOrWhiteSpace(node.AuthoredSource);

    /// <summary>Export the whole document (all pages) as a PDF: selectable text for labels,
    /// 300-DPI images for LaTeX/Mermaid, vector paths for shapes, connectors, and ink.</summary>
    [RelayCommand]
    private async Task ExportPdfAsync()
    {
        if (IsPlacingAsync) return;

        // Flush live view state (incl. the active page's ink) so the PDF matches the screen.
        SyncViewState();
        if (SelectedPage is not null)
            SelectedPage.Page.FloatingInk = InkSerializer.ToBytes(FloatingStrokes);

        var safeName = string.Join("_",
            _document.Manifest.Title.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        if (string.IsNullOrWhiteSpace(safeName)) safeName = "canvas";

        var path = await _dialogs.ShowSaveFileAsync("Export PDF", $"{safeName}.pdf", "PDF Document|*.pdf");
        if (string.IsNullOrWhiteSpace(path)) return;

        IsPlacingAsync = true;
        try
        {
            await _pdfExporter.ExportAsync(_document, BuildPdfTheme(), path);
            await _dialogs.ShowAlertAsync("Export complete", $"PDF saved to:\n{path}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Canvas PDF export failed.");
            await _dialogs.ShowAlertAsync("Export failed", "The PDF could not be created. See the log for details.");
        }
        finally
        {
            IsPlacingAsync = false;
        }
    }

    /// <summary>The document's effective colors (override, else app-theme default) frozen as hex
    /// for the exporter, plus the app theme's page background so a dark theme exports dark pages.</summary>
    private CanvasPdfTheme BuildPdfTheme()
    {
        var overrides = _document.Manifest.ThemeColors;
        string Effective(string key, string fallback) =>
            overrides.TryGetValue(key, out var hex) && !string.IsNullOrWhiteSpace(hex)
                ? hex
                : _canvasTheme.GetThemeDefaultHex(key) ?? fallback;

        var defaults = CanvasPdfTheme.Default;
        return new CanvasPdfTheme(
            // Effective (override-aware) so a Custom app theme exports its page/surface/text colors.
            _canvasTheme.GetEffectiveHex("BackgroundBrush") ?? defaults.PageBackgroundHex,
            Effective(Services.CanvasThemeService.TextKey, defaults.TextHex),
            Effective(Services.CanvasThemeService.AccentKey, defaults.AccentHex),
            Effective(Services.CanvasThemeService.SurfaceKey, defaults.SurfaceHex),
            // Markdown notes now follow the app theme surface/border/text (they match the UI, and the
            // pixel-perfect raster reads the live resources); the sticky chrome uses those colors.
            _canvasTheme.GetEffectiveHex("SurfaceBrush") ?? defaults.SurfaceHex,
            _canvasTheme.GetEffectiveHex("BorderBrush") ?? defaults.StickyStrokeHex,
            _canvasTheme.GetEffectiveHex("TextPrimaryBrush") ?? defaults.TextHex);
    }

    /// <summary>Called from the view's Unloaded (navigation away / app close): drop this document's
    /// runtime color overrides so the rest of the app — and the next canvas — starts from the app
    /// theme's defaults, and release the theme-change subscription (transient VM).</summary>
    public void OnViewUnloaded()
    {
        _themeService.ThemeChanged -= OnAppThemeChanged;
        _canvasTheme.Reset();

        // Flush pending edits — navigating away via the sidebar or a workspace switch must not
        // silently drop canvas changes. Fire-and-forget: Unloaded can't await, and SaveAsync
        // targets the load-time _workspaceId so a workspace switch can't misroute the write.
        if (IsDirty && _workspaceId != Guid.Empty)
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
            _logger.LogError(ex, "Failed to save canvas document {DocumentId} while leaving the editor.",
                _document.Manifest.Id);
        }
    }

    private void SyncViewState()
    {
        _diagram.ShowGrid = ShowGrid;
        _diagram.SnapToGrid = SnapToGrid;
        _diagram.GridSize = GridSize;
        _diagram.ZoomLevel = Zoom;
        _diagram.CanvasWidth = CanvasWidth;
        _diagram.CanvasHeight = CanvasHeight;
    }

    // ----- internal mutators used by the undoable commands (no re-push) -----

    internal void AddNodeInternal(CanvasNodeViewModel node)
    {
        node.Changed -= OnNodeChangedDirty;
        node.Changed += OnNodeChangedDirty;
        if (!_diagram.Nodes.Contains(node.Node)) _diagram.Nodes.Add(node.Node);
        if (!Nodes.Contains(node)) Nodes.Add(node);
        IsDirty = true;
    }

    internal void RemoveNodeInternal(CanvasNodeViewModel node)
    {
        node.Changed -= OnNodeChangedDirty;
        _diagram.Nodes.Remove(node.Node);
        Nodes.Remove(node);
        if (SelectedNode == node) SelectedNode = null;
        IsDirty = true;
    }

    /// <summary>Move a node to a new z-index, keeping the VM collection and the persisted model
    /// list in lockstep. Collection order IS draw order (Canvas ItemsPanel renders in list order).</summary>
    internal void MoveNodeToIndexInternal(CanvasNodeViewModel node, int index)
    {
        var from = Nodes.IndexOf(node);
        if (from < 0 || index < 0 || index >= Nodes.Count || from == index) return;
        Nodes.Move(from, index);

        var modelFrom = _diagram.Nodes.IndexOf(node.Node);
        if (modelFrom >= 0)
        {
            _diagram.Nodes.RemoveAt(modelFrom);
            _diagram.Nodes.Insert(index, node.Node);
        }
        IsDirty = true;
    }

    internal void AddConnectorInternal(CanvasConnectorViewModel connector)
    {
        if (!_diagram.Connectors.Contains(connector.Connector)) _diagram.Connectors.Add(connector.Connector);
        if (!Connectors.Contains(connector)) Connectors.Add(connector);
        IsDirty = true;
    }

    internal void RemoveConnectorInternal(CanvasConnectorViewModel connector)
    {
        connector.Detach();
        _diagram.Connectors.Remove(connector.Connector);
        Connectors.Remove(connector);
        if (SelectedConnector == connector) SelectedConnector = null;
        IsDirty = true;
    }

    private void OnNodeChangedDirty(object? sender, EventArgs e) => IsDirty = true;
}
