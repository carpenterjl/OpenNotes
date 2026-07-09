using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace OpenNotes.Views;

/// <summary>Attached behavior for <see cref="FlowDocumentScrollViewer"/>-based markdown viewers
/// (Markdig.Wpf's <c>MarkdownViewer</c>): normalizes every generated <see cref="FlowDocument"/> to
/// <c>PagePadding = 0</c> and <c>ColumnWidth = ∞</c>. This makes the live viewer's wrap width and
/// text origin exactly the control width — the same geometry <c>MarkdownImageRenderer</c> uses for
/// the PDF raster — so canvas notes, the task-view preview, and the PDF all lay out text
/// identically (and node-bound ink drawn over a note stays aligned in the export).
/// MarkdownViewer regenerates its Document on every Markdown change, hence the property-changed
/// hook rather than a one-shot on Loaded.</summary>
public static class FlowDocumentBehaviors
{
    public static readonly DependencyProperty NormalizeDocumentProperty =
        DependencyProperty.RegisterAttached(
            "NormalizeDocument", typeof(bool), typeof(FlowDocumentBehaviors),
            new PropertyMetadata(false, OnNormalizeDocumentChanged));

    public static bool GetNormalizeDocument(DependencyObject obj) => (bool)obj.GetValue(NormalizeDocumentProperty);
    public static void SetNormalizeDocument(DependencyObject obj, bool value) => obj.SetValue(NormalizeDocumentProperty, value);

    private static void OnNormalizeDocumentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FlowDocumentScrollViewer viewer) return;

        var descriptor = DependencyPropertyDescriptor.FromProperty(
            FlowDocumentScrollViewer.DocumentProperty, typeof(FlowDocumentScrollViewer));

        if ((bool)e.NewValue)
        {
            descriptor.AddValueChanged(viewer, OnDocumentChanged);
            Normalize(viewer.Document);
        }
        else
        {
            descriptor.RemoveValueChanged(viewer, OnDocumentChanged);
        }
    }

    private static void OnDocumentChanged(object? sender, EventArgs e)
    {
        if (sender is FlowDocumentScrollViewer viewer)
            Normalize(viewer.Document);
    }

    private static void Normalize(FlowDocument? document)
    {
        if (document is null) return;
        document.PagePadding = new Thickness(0);
        document.ColumnWidth = double.PositiveInfinity;
    }
}
