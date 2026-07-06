using CommunityToolkit.Mvvm.ComponentModel;

namespace OpenNotes.ViewModels;

public abstract class ViewModelBase : ObservableObject
{
    private bool _isBusy;
    private string _title = string.Empty;

    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    public virtual Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;
    public virtual void Cleanup() { }
}
