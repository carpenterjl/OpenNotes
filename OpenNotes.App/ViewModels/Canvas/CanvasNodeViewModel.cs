using System.Windows.Ink;
using CommunityToolkit.Mvvm.ComponentModel;
using OpenNotes.Models;

namespace OpenNotes.ViewModels.Canvas;

/// <summary>Bindable wrapper over a <see cref="DiagramNode"/> (write-through: setters update the
/// underlying model so the owning <see cref="DiagramModel"/> is always ready to persist).</summary>
public partial class CanvasNodeViewModel : ObservableObject
{
    public DiagramNode Node { get; }
    public Guid Id => Node.Id;

    [ObservableProperty] private double _x;
    [ObservableProperty] private double _y;
    [ObservableProperty] private double _width;
    [ObservableProperty] private double _height;
    [ObservableProperty] private string _label;
    [ObservableProperty] private NodeShape _shape;
    [ObservableProperty] private string _fillColor;
    [ObservableProperty] private string _strokeColor;
    [ObservableProperty] private bool _showBorder;
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private string? _imagePath;
    [ObservableProperty] private string? _latexContent;
    [ObservableProperty] private string? _svgContent;
    [ObservableProperty] private Guid? _linkedTaskId;
    [ObservableProperty] private Guid? _sourceContentBlockId;
    [ObservableProperty] private string? _blockKind;
    [ObservableProperty] private string? _authoredSource;
    [ObservableProperty] private string? _authoredLanguage;

    /// <summary>Transient UI-only state (not persisted): true while the inline label TextBox is
    /// active. When false the label is a click-through TextBlock so the node's Thumb can still be
    /// selected/dragged with a single click.</summary>
    [ObservableProperty] private bool _isEditingLabel;

    /// <summary>Back-reference to the owning editor VM so the node's context menu can invoke its
    /// commands (the ContextMenu lives outside the visual tree). Not persisted.</summary>
    public CanvasEditorViewModel? Owner { get; set; }

    /// <summary>True for a node that is (or can be) linked to a task content block.</summary>
    public bool IsBlockBacked => BlockKind is not null;

    /// <summary>Node-bound ink strokes in node-local coordinates (rendered by an InkPresenter inside
    /// the node template, so they move with the node). Write-through: every change re-serializes to
    /// <see cref="DiagramNode.InkData"/> so the model stays persist-ready.</summary>
    public StrokeCollection InkStrokes { get; }

    /// <summary>Raised when geometry/appearance changes — drives connector reflow and dirty tracking.</summary>
    public event EventHandler? Changed;

    public CanvasNodeViewModel(DiagramNode node)
    {
        Node = node;

        // Self-heal documents saved while the Shape write-through bug existed: a rendered
        // Mermaid node persisted as Svg with an image path but no SVG payload is really an
        // Image node — without this it materializes invisible.
        if (node.Shape == NodeShape.Svg &&
            string.IsNullOrEmpty(node.SvgContent) &&
            !string.IsNullOrEmpty(node.ImagePath))
            node.Shape = NodeShape.Image;

        InkStrokes = InkSerializer.FromBytes(node.InkData);
        InkStrokes.StrokesChanged += (_, _) =>
        {
            Node.InkData = InkSerializer.ToBytes(InkStrokes);
            RaiseChanged();
        };
        _x = node.X;
        _y = node.Y;
        _width = node.Width;
        _height = node.Height;
        _label = node.Label;
        _shape = node.Shape;
        _fillColor = node.FillColor;
        _strokeColor = node.StrokeColor;
        _showBorder = node.ShowBorder;
        _imagePath = node.ImagePath;
        _latexContent = node.LatexContent;
        _svgContent = node.SvgContent;
        _linkedTaskId = node.LinkedTaskId;
        _sourceContentBlockId = node.SourceContentBlockId;
        _blockKind = node.BlockKind;
        _authoredSource = node.AuthoredSource;
        _authoredLanguage = node.AuthoredLanguage;
    }

    public double CenterX => X + Width / 2;
    public double CenterY => Y + Height / 2;

    /// <summary>True for a bare text label (no shape chrome).</summary>
    public bool IsTextOnly => Shape == NodeShape.Text;

    /// <summary>A markdown snapshot node (sticky note) — renders formatted markdown via a
    /// MarkdownViewer in the node template instead of the plain label.</summary>
    public bool IsMarkdownNote => Shape == NodeShape.StickyNote && BlockKind == "markdown";

    /// <summary>Whether the shared centered, editable label is shown. Snapshot shapes that render
    /// their own content (image/svg/latex/checklist/markdown) suppress it to avoid a duplicate
    /// overlay.</summary>
    public bool ShowInlineLabel =>
        Shape is not (NodeShape.Latex or NodeShape.Image or NodeShape.Svg or NodeShape.Checklist) &&
        !IsMarkdownNote;

    /// <summary>Read-only label display: shown whenever not actively editing, so a single click
    /// falls through to the Thumb (select + drag) instead of the TextBox (focus + caret).</summary>
    public bool ShowLabelTextBlock => ShowInlineLabel && !IsEditingLabel;

    /// <summary>Editable label TextBox: shown only while double-click has activated edit mode.</summary>
    public bool ShowLabelTextBox => ShowInlineLabel && IsEditingLabel;

    partial void OnShapeChanged(NodeShape value)
    {
        // Write-through like every other property — without it a runtime shape change
        // (e.g. a Mermaid node flipping Svg → Image after its PNG render) displays fine
        // in-session but persists the OLD shape, so the node comes back invisible on reload.
        Node.Shape = value;
        RaiseChanged();
        OnPropertyChanged(nameof(IsTextOnly));
        OnPropertyChanged(nameof(IsMarkdownNote));
        OnPropertyChanged(nameof(ShowInlineLabel));
        OnPropertyChanged(nameof(ShowLabelTextBlock));
        OnPropertyChanged(nameof(ShowLabelTextBox));
    }

    partial void OnIsEditingLabelChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowLabelTextBlock));
        OnPropertyChanged(nameof(ShowLabelTextBox));
    }

    partial void OnXChanged(double value) { Node.X = value; OnPropertyChanged(nameof(CenterX)); RaiseChanged(); }
    partial void OnYChanged(double value) { Node.Y = value; OnPropertyChanged(nameof(CenterY)); RaiseChanged(); }
    partial void OnWidthChanged(double value) { Node.Width = value; OnPropertyChanged(nameof(CenterX)); RaiseChanged(); }
    partial void OnHeightChanged(double value) { Node.Height = value; OnPropertyChanged(nameof(CenterY)); RaiseChanged(); }
    partial void OnLabelChanged(string value) { Node.Label = value; RaiseChanged(); }
    partial void OnFillColorChanged(string value) { Node.FillColor = value; RaiseChanged(); }
    partial void OnStrokeColorChanged(string value) { Node.StrokeColor = value; RaiseChanged(); }
    partial void OnShowBorderChanged(bool value) { Node.ShowBorder = value; RaiseChanged(); }
    partial void OnImagePathChanged(string? value) { Node.ImagePath = value; RaiseChanged(); }
    partial void OnLatexContentChanged(string? value) { Node.LatexContent = value; RaiseChanged(); }
    partial void OnSvgContentChanged(string? value) { Node.SvgContent = value; RaiseChanged(); }
    partial void OnLinkedTaskIdChanged(Guid? value) { Node.LinkedTaskId = value; RaiseChanged(); }
    partial void OnSourceContentBlockIdChanged(Guid? value) { Node.SourceContentBlockId = value; RaiseChanged(); }
    partial void OnBlockKindChanged(string? value)
    {
        Node.BlockKind = value;
        OnPropertyChanged(nameof(IsBlockBacked));
        OnPropertyChanged(nameof(IsMarkdownNote));
        OnPropertyChanged(nameof(ShowInlineLabel));
        OnPropertyChanged(nameof(ShowLabelTextBlock));
        OnPropertyChanged(nameof(ShowLabelTextBox));
        RaiseChanged();
    }
    partial void OnAuthoredSourceChanged(string? value) { Node.AuthoredSource = value; RaiseChanged(); }
    partial void OnAuthoredLanguageChanged(string? value) { Node.AuthoredLanguage = value; RaiseChanged(); }

    private void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}
