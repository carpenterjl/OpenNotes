using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using OpenNotes.ViewModels;
using OpenNotes.ViewModels.Canvas;

namespace OpenNotes.Views;

public partial class CanvasEditorView : UserControl
{
    private const double MinNodeSize = 24;

    private double _dragOldX;
    private double _dragOldY;
    private CanvasNodeViewModel? _connectorSource;

    private double _resizeOldX;
    private double _resizeOldY;
    private double _resizeOldWidth;
    private double _resizeOldHeight;

    public CanvasEditorView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            if (DataContext is CanvasEditorViewModel vm)
                vm.ThumbnailProvider = CaptureThumbnail;
        };
    }

    /// <summary>Render the current canvas into a small PNG for the page overview grid. Returns null
    /// when nothing meaningful can be captured (view not laid out yet, or the canvas is hidden behind
    /// the overview — capturing then would clobber a good thumbnail with a blank one).</summary>
    private byte[]? CaptureThumbnail()
    {
        const double targetWidth = 480; // decoded at ~260px card width; 480 keeps text legible
        if (!CanvasRoot.IsVisible || CanvasRoot.ActualWidth < 1 || CanvasRoot.ActualHeight < 1)
            return null;

        try
        {
            double scale = Math.Min(1.0, targetWidth / CanvasRoot.ActualWidth);
            int width = (int)Math.Ceiling(CanvasRoot.ActualWidth * scale);
            int height = (int)Math.Ceiling(CanvasRoot.ActualHeight * scale);

            // VisualBrush + DrawingVisual instead of rendering CanvasRoot directly: RenderTargetBitmap
            // captures at the element's layout size, and we want a scaled-down bitmap without touching
            // the live element's transforms.
            var visual = new System.Windows.Media.DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(System.Windows.Media.Brushes.White, null, new Rect(0, 0, width, height));
                dc.DrawRectangle(new System.Windows.Media.VisualBrush(CanvasRoot) { Stretch = System.Windows.Media.Stretch.Uniform },
                    null, new Rect(0, 0, width, height));
            }

            var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(
                width, height, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            bitmap.Render(visual);

            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
            using var stream = new System.IO.MemoryStream();
            encoder.Save(stream);
            return stream.ToArray();
        }
        catch
        {
            return null; // a failed thumbnail must never break page switching or saving
        }
    }

    // Leaving the canvas (navigation or app close): drop this document's runtime color overrides
    // so the next canvas — and every DynamicResource CanvasXxxBrush user — falls back to the theme.
    private void View_Unloaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is CanvasEditorViewModel vm)
            vm.OnViewUnloaded();
    }

    // A stroke finished on the floating-ink overlay: let the VM decide whether it stays floating
    // or gets re-parented into the node it was drawn on (fully-contained bounds), undoably.
    private void InkOverlay_StrokeCollected(object sender, InkCanvasStrokeCollectedEventArgs e)
    {
        if (DataContext is CanvasEditorViewModel vm)
            vm.HandleStrokeCollected(e.Stroke);
    }

    // Cancel the InkCanvas's own (non-undoable) erase and route it through the undo stack instead.
    private void InkOverlay_StrokeErasing(object sender, InkCanvasStrokeErasingEventArgs e)
    {
        if (DataContext is CanvasEditorViewModel vm)
        {
            e.Cancel = true;
            vm.EraseStroke(e.Stroke);
        }
    }

    // The InkCanvas eraser only hit-tests its own floating strokes; node-bound ink lives in
    // hit-test-invisible InkPresenters inside the nodes. While the ⌫ tool is active, tunneling
    // mouse events hit-test node ink manually so one sweep erases both layers.
    private void InkOverlay_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is CanvasEditorViewModel { ActiveTool: CanvasTool.InkEraser } vm)
        {
            var p = e.GetPosition(CanvasRoot);
            vm.EraseNodeInkAt(p.X, p.Y);
        }
    }

    private void InkOverlay_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed &&
            DataContext is CanvasEditorViewModel { ActiveTool: CanvasTool.InkEraser } vm)
        {
            var p = e.GetPosition(CanvasRoot);
            vm.EraseNodeInkAt(p.X, p.Y);
        }
    }

    // While an ink tool is active the overlay covers every node, so a right-click would never
    // reach a node's ContextMenu (e.g. "Clear ink"). Re-route it to the topmost node under the
    // cursor and open that node's menu manually.
    private void InkOverlay_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not CanvasEditorViewModel vm || !vm.IsInkToolActive) return;
        var p = e.GetPosition(CanvasRoot);
        var node = vm.Nodes.LastOrDefault(n => new Rect(n.X, n.Y, n.Width, n.Height).Contains(p));
        if (node is null) return;

        if (NodesItemsControl.ItemContainerGenerator.ContainerFromItem(node) is ContentPresenter cp &&
            System.Windows.Media.VisualTreeHelper.GetChildrenCount(cp) > 0 &&
            System.Windows.Media.VisualTreeHelper.GetChild(cp, 0) is Thumb { ContextMenu: { } menu } thumb)
        {
            menu.PlacementTarget = thumb;
            menu.IsOpen = true;
            e.Handled = true;
        }
    }

    // ----- drag-to-size shape creation state (shape/text tools only) -----
    private const double DragSizeThreshold = 8;
    private Point _shapeDragStart;
    private bool _isShapeDragging;

    // Click on empty canvas: deselect (Select/Connector), create a task (NewTask), or begin a
    // drag-to-size shape placement — a bare click (sub-threshold drag) still places the default size.
    private async void Surface_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not CanvasEditorViewModel vm) return;
        var p = e.GetPosition(CanvasRoot); // CanvasRoot's local space => already un-zoomed coords

        if (vm.ActiveTool is CanvasTool.Select or CanvasTool.Connector)
        {
            vm.SelectNode(null);
            vm.SelectConnector(null);
            _connectorSource = null;
        }
        else if (vm.ActiveTool == CanvasTool.NewTask)
        {
            await vm.PlaceNewTaskNodeAsync(p.X, p.Y);
        }
        else
        {
            _shapeDragStart = p;
            _isShapeDragging = GridSurface.CaptureMouse();
            if (!_isShapeDragging)
                vm.PlaceNode(p.X, p.Y); // capture failed → old immediate placement
        }
    }

    private void Surface_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isShapeDragging) return;
        var r = new Rect(_shapeDragStart, e.GetPosition(CanvasRoot)); // normalizes any drag direction
        if (r.Width < DragSizeThreshold && r.Height < DragSizeThreshold)
        {
            DragSizePreview.Visibility = Visibility.Collapsed;
            return;
        }
        Canvas.SetLeft(DragSizePreview, r.X);
        Canvas.SetTop(DragSizePreview, r.Y);
        DragSizePreview.Width = r.Width;
        DragSizePreview.Height = r.Height;
        DragSizePreview.Visibility = Visibility.Visible;
    }

    private void Surface_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isShapeDragging) return;
        _isShapeDragging = false;
        DragSizePreview.Visibility = Visibility.Collapsed;
        GridSurface.ReleaseMouseCapture();

        if (DataContext is not CanvasEditorViewModel vm) return;
        var r = new Rect(_shapeDragStart, e.GetPosition(CanvasRoot));
        if (r.Width < DragSizeThreshold && r.Height < DragSizeThreshold)
            vm.PlaceNode(_shapeDragStart.X, _shapeDragStart.Y); // bare click → default size
        else
            vm.PlaceNodeSized(r.X, r.Y, r.Width, r.Height);
    }

    // Escape mid-drag cancels the pending placement (releasing capture routes the cleanup
    // through Surface_LostMouseCapture).
    private void View_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _isShapeDragging)
        {
            GridSurface.ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    // Capture loss (Alt-Tab, Escape below, a dialog stealing focus, …) cancels the pending
    // placement without placing anything; the tool stays armed so the user can retry.
    private void Surface_LostMouseCapture(object sender, MouseEventArgs e)
    {
        _isShapeDragging = false;
        DragSizePreview.Visibility = Visibility.Collapsed;
    }

    // The surface's right-click menu exists only for the Select tool; it creates at the clicked
    // canvas point, which is captured here (before the menu opens) into the VM's context point.
    private void Surface_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (DataContext is not CanvasEditorViewModel vm || vm.ActiveTool != CanvasTool.Select)
        {
            e.Handled = true;
            return;
        }
        var p = Mouse.GetPosition(CanvasRoot);
        vm.SetContextPoint(p.X, p.Y);
    }

    // Double-click a task-link node → open that task's editor. Double-click any other node with an
    // inline label → enter label edit mode (single-click alone selects/drags, matching every other
    // node shape; the label TextBox only takes over hit-testing once editing is explicitly entered).
    private void Node_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Thumb { Tag: CanvasNodeViewModel node } thumb || DataContext is not CanvasEditorViewModel vm)
            return;

        if (node.LinkedTaskId is not null)
        {
            vm.OpenLinkedTaskCommand.Execute(node);
            e.Handled = true;
            return;
        }

        if (node.ShowInlineLabel)
        {
            node.IsEditingLabel = true;
            e.Handled = true;
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                if (thumb.Template.FindName("InlineLabelBox", thumb) is TextBox box)
                {
                    box.Focus();
                    box.SelectAll();
                }
            }));
            return;
        }

        // Snapshot nodes without an inline label (markdown/latex/code/mermaid/image/checklist):
        // double-click opens their content editor dialog directly.
        if (node.IsBlockBacked)
        {
            vm.EditNodeContentCommand.Execute(node);
            e.Handled = true;
        }
    }

    private void InlineLabel_LostFocus(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is CanvasNodeViewModel node)
            node.IsEditingLabel = false;
    }

    private void InlineLabel_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Escape)
        {
            e.Handled = true;
            Keyboard.ClearFocus(); // triggers LostFocus → exits edit mode
        }
    }

    private void Node_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Thumb { Tag: CanvasNodeViewModel node } || DataContext is not CanvasEditorViewModel vm)
            return;

        if (vm.ActiveTool == CanvasTool.Connector)
        {
            if (_connectorSource is null)
            {
                _connectorSource = node;
                vm.SelectNode(node);
            }
            else
            {
                vm.AddConnector(_connectorSource, node);
                _connectorSource = null;
            }
            e.Handled = true; // don't start a drag / caret edit while wiring connectors
            return;
        }

        vm.SelectNode(node); // let the event continue so the Thumb can drag / the TextBox can edit
    }

    private void Connector_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: CanvasConnectorViewModel connector } && DataContext is CanvasEditorViewModel vm)
        {
            vm.SelectConnector(connector);
            e.Handled = true; // don't bubble to Surface_MouseLeftButtonDown and immediately deselect
        }
    }

    private void Node_DragStarted(object sender, DragStartedEventArgs e)
    {
        if (sender is Thumb { Tag: CanvasNodeViewModel node })
        {
            _dragOldX = node.X;
            _dragOldY = node.Y;
        }
    }

    private void Node_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (sender is Thumb { Tag: CanvasNodeViewModel node })
        {
            node.X += e.HorizontalChange;
            node.Y += e.VerticalChange;
        }
    }

    private void Node_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (sender is Thumb { Tag: CanvasNodeViewModel node } && DataContext is CanvasEditorViewModel vm)
            vm.CommitMove(node, _dragOldX, _dragOldY);
    }

    // Nested inside the same Thumb-based node template as the outer draggable Thumb; without the
    // e.Handled = true below (in Resize_DragStarted/DragDelta/DragCompleted — Thumb-specific bubbling
    // events raised only after this Thumb's own drag has already started), dragging a resize grip
    // would also fire the outer Thumb's own move logic, causing the node to move while it resizes.
    // Do NOT intercept PreviewMouseLeftButtonDown to fix this instead: that event is what Thumb's own
    // drag-initiation/mouse-capture logic is wired to, and marking it handled during the tunneling
    // phase breaks the capture handshake for both this Thumb and the outer one (dragging stops working
    // entirely) — confirmed the hard way.
    private void Resize_DragStarted(object sender, DragStartedEventArgs e)
    {
        if (sender is Thumb { Tag: CanvasNodeViewModel node })
        {
            _resizeOldX = node.X;
            _resizeOldY = node.Y;
            _resizeOldWidth = node.Width;
            _resizeOldHeight = node.Height;
        }
        e.Handled = true;
    }

    private void Resize_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (sender is not Thumb { Tag: CanvasNodeViewModel node } thumb)
            return;

        var (left, top, right, bottom) = GetHandleSides(thumb.Name);

        if (left)
        {
            double newWidth = node.Width - e.HorizontalChange;
            if (newWidth < MinNodeSize)
            {
                node.X += node.Width - MinNodeSize;
                node.Width = MinNodeSize;
            }
            else
            {
                node.X += e.HorizontalChange;
                node.Width = newWidth;
            }
        }
        else if (right)
        {
            node.Width = System.Math.Max(MinNodeSize, node.Width + e.HorizontalChange);
        }

        if (top)
        {
            double newHeight = node.Height - e.VerticalChange;
            if (newHeight < MinNodeSize)
            {
                node.Y += node.Height - MinNodeSize;
                node.Height = MinNodeSize;
            }
            else
            {
                node.Y += e.VerticalChange;
                node.Height = newHeight;
            }
        }
        else if (bottom)
        {
            node.Height = System.Math.Max(MinNodeSize, node.Height + e.VerticalChange);
        }

        e.Handled = true;
    }

    private void Resize_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (sender is Thumb { Tag: CanvasNodeViewModel node } && DataContext is CanvasEditorViewModel vm)
        {
            node.X = vm.Snap(node.X);
            node.Y = vm.Snap(node.Y);
            node.Width = System.Math.Max(MinNodeSize, vm.Snap(node.Width));
            node.Height = System.Math.Max(MinNodeSize, vm.Snap(node.Height));

            if (node.X != _resizeOldX || node.Y != _resizeOldY ||
                node.Width != _resizeOldWidth || node.Height != _resizeOldHeight)
            {
                vm.CommitResize(node, _resizeOldX, _resizeOldY, _resizeOldWidth, _resizeOldHeight);
            }
        }
        e.Handled = true;
    }

    private static (bool Left, bool Top, bool Right, bool Bottom) GetHandleSides(string handleName) => handleName switch
    {
        "ResizeTopLeft" => (true, true, false, false),
        "ResizeTopRight" => (false, true, true, false),
        "ResizeBottomLeft" => (true, false, false, true),
        "ResizeBottomRight" => (false, false, true, true),
        "ResizeTop" => (false, true, false, false),
        "ResizeBottom" => (false, false, false, true),
        "ResizeLeft" => (true, false, false, false),
        "ResizeRight" => (false, false, true, false),
        _ => (false, false, false, false),
    };
}
