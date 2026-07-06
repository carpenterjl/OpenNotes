using CommunityToolkit.Mvvm.ComponentModel;
using OpenNotes.Models.Blocks;

namespace OpenNotes.ViewModels.Blocks;

public partial class MermaidBlockViewModel : BlockViewModelBase
{
    [ObservableProperty] private string _definition;
    [ObservableProperty] private bool _isPreviewMode = true;
    [ObservableProperty] private string? _errorMessage;

    public MermaidBlockViewModel(MermaidBlock block) : base(block)
    {
        _definition = block.Definition;
    }

    partial void OnDefinitionChanged(string value) => RaiseContentChanged();

    public override ContentBlock GetUpdatedBlock()
    {
        ((MermaidBlock)Block).Definition = Definition;
        return Block;
    }
}
