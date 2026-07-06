using CommunityToolkit.Mvvm.ComponentModel;
using OpenNotes.Models.Blocks;

namespace OpenNotes.ViewModels.Blocks;

public partial class MarkdownBlockViewModel : BlockViewModelBase
{
    [ObservableProperty] private string _markdown;
    [ObservableProperty] private bool _isPreviewMode = true;

    public MarkdownBlockViewModel(MarkdownBlock block) : base(block)
    {
        _markdown = block.Markdown;
    }

    partial void OnMarkdownChanged(string value) => RaiseContentChanged();

    public override ContentBlock GetUpdatedBlock()
    {
        ((MarkdownBlock)Block).Markdown = Markdown;
        return Block;
    }
}
