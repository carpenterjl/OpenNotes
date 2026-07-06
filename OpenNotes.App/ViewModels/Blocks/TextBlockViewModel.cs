using CommunityToolkit.Mvvm.ComponentModel;
using OpenNotes.Models.Blocks;

namespace OpenNotes.ViewModels.Blocks;

public partial class TextBlockViewModel : BlockViewModelBase
{
    [ObservableProperty] private string _content;

    public TextBlockViewModel(TextBlock block) : base(block)
    {
        _content = block.Content;
    }

    partial void OnContentChanged(string value) => RaiseContentChanged();

    public override ContentBlock GetUpdatedBlock()
    {
        ((TextBlock)Block).Content = Content;
        return Block;
    }
}
