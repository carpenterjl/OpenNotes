namespace OpenNotes.Interfaces;

public interface INavigationService
{
    object? CurrentView { get; }
    bool CanGoBack { get; }
    bool CanGoForward { get; }

    event EventHandler<object?>? CurrentViewChanged;

    void NavigateTo<TViewModel>(Action<TViewModel>? configure = null) where TViewModel : class;
    void GoBack();
    void GoForward();
    void ClearHistory();
}
