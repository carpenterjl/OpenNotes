using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenNotes.Interfaces;
using OpenNotes.Models.Blocks;

namespace OpenNotes.ViewModels.Blocks;

public partial class ImageBlockViewModel : BlockViewModelBase
{
    private readonly IDialogService _dialogs;

    [ObservableProperty] private string _filePath;
    [ObservableProperty] private string? _caption;
    [ObservableProperty] private double? _widthPercent;

    public ImageBlockViewModel(ImageBlock block, IDialogService dialogs) : base(block)
    {
        _dialogs = dialogs;
        _filePath = block.FilePath;
        _caption = block.Caption;
        _widthPercent = block.WidthPercent;
    }

    [RelayCommand]
    private async Task BrowseImageAsync()
    {
        var path = await _dialogs.ShowOpenFileAsync("Select Image",
            "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp|All Files|*.*");
        if (path is not null)
        {
            FilePath = path;
            RaiseContentChanged();
        }
    }

    public override ContentBlock GetUpdatedBlock()
    {
        var b = (ImageBlock)Block;
        b.FilePath = FilePath;
        b.Caption = Caption;
        b.WidthPercent = WidthPercent;
        return Block;
    }
}
