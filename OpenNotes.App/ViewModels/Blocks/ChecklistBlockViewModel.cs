using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenNotes.Models;
using OpenNotes.Models.Blocks;

namespace OpenNotes.ViewModels.Blocks;

public partial class ChecklistItemViewModel : ObservableObject
{
    private readonly ChecklistItem _item;

    [ObservableProperty] private string _text;
    [ObservableProperty] private bool _isCompleted;

    /// <summary>Raised whenever the item's text or completion state changes, so the
    /// owning block can mark the task dirty and trigger autosave.</summary>
    public Action? Changed { get; set; }

    public ChecklistItemViewModel(ChecklistItem item)
    {
        _item = item;
        _text = item.Text;
        _isCompleted = item.IsCompleted;
    }

    partial void OnIsCompletedChanged(bool value)
    {
        _item.IsCompleted = value;
        _item.CompletedAt = value ? DateTime.UtcNow : null;
        Changed?.Invoke();
    }

    partial void OnTextChanged(string value)
    {
        _item.Text = value;
        Changed?.Invoke();
    }

    public ChecklistItem GetModel() => _item;
}

public partial class ChecklistBlockViewModel : BlockViewModelBase
{
    public ObservableCollection<ChecklistItemViewModel> Items { get; } = [];

    public ChecklistBlockViewModel(ChecklistBlock block) : base(block)
    {
        foreach (var item in block.Items.OrderBy(i => i.Order))
            Items.Add(Track(new ChecklistItemViewModel(item)));
    }

    private ChecklistItemViewModel Track(ChecklistItemViewModel item)
    {
        item.Changed = RaiseContentChanged;
        return item;
    }

    [RelayCommand]
    private void AddItem()
    {
        var item = new ChecklistItem { Order = Items.Count, Text = "New item" };
        Items.Add(Track(new ChecklistItemViewModel(item)));
        RaiseContentChanged();
    }

    [RelayCommand]
    private void RemoveItem(ChecklistItemViewModel item)
    {
        Items.Remove(item);
        for (int i = 0; i < Items.Count; i++)
            Items[i].GetModel().Order = i;
        RaiseContentChanged();
    }

    public override ContentBlock GetUpdatedBlock()
    {
        ((ChecklistBlock)Block).Items = Items.Select(i => i.GetModel()).ToList();
        return Block;
    }
}
