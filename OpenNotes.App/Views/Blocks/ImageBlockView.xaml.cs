using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using OpenNotes.ViewModels.Blocks;

namespace OpenNotes.Views.Blocks;

public partial class ImageBlockView : UserControl
{
    public ImageBlockView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is ImageBlockViewModel vm)
        {
            vm.PropertyChanged += (_, pe) =>
            {
                if (pe.PropertyName == nameof(vm.FilePath))
                    LoadImage(vm.FilePath);
            };
            LoadImage(vm.FilePath);
        }
    }

    private void LoadImage(string? path)
    {
        if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
        {
            PreviewImage.Source = null;
            return;
        }

        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            // Bypass WPF's per-URI bitmap cache — the file's content can change under the same path.
            bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            PreviewImage.Source = bmp;
        }
        catch
        {
            PreviewImage.Source = null;
        }
    }
}
