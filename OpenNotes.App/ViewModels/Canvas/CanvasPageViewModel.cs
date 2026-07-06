using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using OpenNotes.Models;

namespace OpenNotes.ViewModels.Canvas;

/// <summary>Lightweight wrapper around one <see cref="CanvasPage"/> for the page tab strip and the
/// grid overview. Holds only metadata and a static thumbnail — never live node ViewModels, so 50
/// pages in the overview cost 50 small bitmaps, not 50 canvases (see UI-virtualization notes in the
/// canvas architecture plan). The active page's nodes are materialized by
/// <see cref="CanvasEditorViewModel"/> on demand.</summary>
public partial class CanvasPageViewModel : ObservableObject
{
    public CanvasPageViewModel(CanvasPage page)
    {
        Page = page;
        _title = page.Info.Title;
        RefreshThumbnail();
    }

    public CanvasPage Page { get; }
    public Guid Id => Page.Info.Id;

    [ObservableProperty] private string _title;
    [ObservableProperty] private bool _isActive;
    [ObservableProperty] private ImageSource? _thumbnailSource;

    public int NodeCount => Page.Diagram.Nodes.Count;

    partial void OnTitleChanged(string value)
    {
        Page.Info.Title = value;
        Page.Diagram.Title = value;
    }

    /// <summary>Store freshly captured PNG bytes and update the displayed bitmap.</summary>
    public void SetThumbnail(byte[]? png)
    {
        Page.Thumbnail = png;
        RefreshThumbnail();
    }

    public void RefreshThumbnail()
    {
        OnPropertyChanged(nameof(NodeCount));
        if (Page.Thumbnail is not { Length: > 0 } bytes)
        {
            ThumbnailSource = null;
            return;
        }

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad; // decode now; don't hold the stream
            image.StreamSource = new MemoryStream(bytes);
            image.EndInit();
            image.Freeze();
            ThumbnailSource = image;
        }
        catch
        {
            ThumbnailSource = null; // a corrupt cached thumbnail must never break the overview
        }
    }
}
