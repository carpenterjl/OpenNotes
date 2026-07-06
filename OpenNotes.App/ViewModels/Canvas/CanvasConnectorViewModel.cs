using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using OpenNotes.Models;

namespace OpenNotes.ViewModels.Canvas;

/// <summary>Bindable wrapper over a <see cref="DiagramConnector"/> that keeps its endpoints
/// anchored to the source/target node borders and re-routes whenever either node moves.</summary>
public partial class CanvasConnectorViewModel : ObservableObject
{
    public DiagramConnector Connector { get; }
    public Guid Id => Connector.Id;
    private readonly CanvasNodeViewModel _source;
    private readonly CanvasNodeViewModel _target;

    [ObservableProperty] private Point _startPoint;
    [ObservableProperty] private Point _endPoint;
    [ObservableProperty] private PointCollection _arrowHead = [];
    [ObservableProperty] private bool _isSelected;

    /// <summary>Back-reference to the owning editor VM so the connector's context menu can invoke
    /// its commands (the ContextMenu lives outside the visual tree). Not persisted.</summary>
    public CanvasEditorViewModel? Owner { get; set; }

    public CanvasNodeViewModel Source => _source;
    public CanvasNodeViewModel Target => _target;
    public string Color => Connector.Color;
    public double Thickness => Connector.Thickness;

    /// <summary>Dash pattern for the line, driven by <see cref="ConnectorStyle"/>. Empty = solid.</summary>
    public DoubleCollection StrokeDashArray => Connector.Style switch
    {
        ConnectorStyle.Dashed => [4, 2],
        ConnectorStyle.Dotted => [1, 2],
        _ => []
    };

    public CanvasConnectorViewModel(DiagramConnector connector, CanvasNodeViewModel source, CanvasNodeViewModel target)
    {
        Connector = connector;
        _source = source;
        _target = target;

        _source.Changed += OnEndpointChanged;
        _target.Changed += OnEndpointChanged;
        Recompute();
    }

    /// <summary>Detach from node events (call when the connector is removed).</summary>
    public void Detach()
    {
        _source.Changed -= OnEndpointChanged;
        _target.Changed -= OnEndpointChanged;
    }

    private void OnEndpointChanged(object? sender, EventArgs e) => Recompute();

    private void Recompute()
    {
        var (sx, sy) = BorderPoint(_source.CenterX, _source.CenterY, _source.Width, _source.Height, _target.CenterX, _target.CenterY);
        var (ex, ey) = BorderPoint(_target.CenterX, _target.CenterY, _target.Width, _target.Height, _source.CenterX, _source.CenterY);
        StartPoint = new Point(sx, sy);
        EndPoint = new Point(ex, ey);
        ArrowHead = BuildArrowHead(sx, sy, ex, ey);
    }

    /// <summary>The point on a node's rectangular border along the ray from its center toward
    /// (<paramref name="towardX"/>, <paramref name="towardY"/>). Pure/testable.</summary>
    public static (double X, double Y) BorderPoint(
        double cx, double cy, double width, double height, double towardX, double towardY)
    {
        double dx = towardX - cx, dy = towardY - cy;
        if (dx == 0 && dy == 0) return (cx, cy);

        double halfW = width / 2, halfH = height / 2;
        double scaleX = dx != 0 ? halfW / Math.Abs(dx) : double.PositiveInfinity;
        double scaleY = dy != 0 ? halfH / Math.Abs(dy) : double.PositiveInfinity;
        double scale = Math.Min(scaleX, scaleY);
        return (cx + dx * scale, cy + dy * scale);
    }

    private static PointCollection BuildArrowHead(double sx, double sy, double ex, double ey)
    {
        const double size = 10, spread = 0.4;
        double ang = Math.Atan2(ey - sy, ex - sx);
        double a1 = ang + Math.PI - spread;
        double a2 = ang + Math.PI + spread;
        return
        [
            new Point(ex, ey),
            new Point(ex + size * Math.Cos(a1), ey + size * Math.Sin(a1)),
            new Point(ex + size * Math.Cos(a2), ey + size * Math.Sin(a2)),
        ];
    }
}
