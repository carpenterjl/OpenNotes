using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenNotes.Models.Blocks;

namespace OpenNotes.ViewModels.Blocks;

public abstract partial class BlockViewModelBase : ObservableObject
{
    public ContentBlock Block { get; }

    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private bool _isHovered;

    /// <summary>Bindable editor height for the block content. <see cref="double.NaN"/> = auto.</summary>
    [ObservableProperty] private double _editorHeight;

    public Guid Id => Block.Id;
    public int Order => Block.Order;

    public event EventHandler<BlockViewModelBase>? DeleteRequested;
    public event EventHandler<(BlockViewModelBase Block, int Direction)>? MoveRequested;
    public event EventHandler? ContentChanged;

    protected BlockViewModelBase(ContentBlock block)
    {
        Block = block;
        _editorHeight = block.Height ?? double.NaN;
    }

    /// <summary>Persist the current editor height back onto the model. Called on save.</summary>
    public void PersistHeight() =>
        Block.Height = double.IsNaN(EditorHeight) ? null : EditorHeight;

    [RelayCommand]
    private void Delete() => DeleteRequested?.Invoke(this, this);

    [RelayCommand]
    private void MoveUp() => MoveRequested?.Invoke(this, (this, -1));

    [RelayCommand]
    private void MoveDown() => MoveRequested?.Invoke(this, (this, 1));

    [RelayCommand]
    private void BeginEdit() => IsEditing = true;

    [RelayCommand]
    private void CommitEdit()
    {
        IsEditing = false;
        ContentChanged?.Invoke(this, EventArgs.Empty);
    }

    protected void RaiseContentChanged() => ContentChanged?.Invoke(this, EventArgs.Empty);

    public abstract ContentBlock GetUpdatedBlock();
}
