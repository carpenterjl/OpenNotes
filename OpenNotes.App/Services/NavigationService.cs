using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenNotes.Interfaces;

namespace OpenNotes.Services;

public class NavigationService : INavigationService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<NavigationService> _logger;

    private readonly Stack<object> _backStack = new();
    private readonly Stack<object> _forwardStack = new();
    private object? _current;

    public object? CurrentView => _current;
    public bool CanGoBack => _backStack.Count > 0;
    public bool CanGoForward => _forwardStack.Count > 0;

    public event EventHandler<object?>? CurrentViewChanged;

    public NavigationService(IServiceProvider services, ILogger<NavigationService> logger)
    {
        _services = services;
        _logger = logger;
    }

    public void NavigateTo<TViewModel>(Action<TViewModel>? configure = null) where TViewModel : class
    {
        if (_current is not null)
        {
            _backStack.Push(_current);
            _forwardStack.Clear();
        }

        var vm = _services.GetRequiredService<TViewModel>();
        configure?.Invoke(vm);
        _current = vm;
        CurrentViewChanged?.Invoke(this, _current);
        _logger.LogDebug("Navigated to {ViewModel}", typeof(TViewModel).Name);

        // Drive the ViewModel lifecycle so views populate their data on navigation.
        if (vm is ViewModels.ViewModelBase vmBase)
            _ = InitializeViewModelAsync(vmBase);
    }

    private async Task InitializeViewModelAsync(ViewModels.ViewModelBase vm)
    {
        try
        {
            await vm.InitializeAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize {ViewModel}", vm.GetType().Name);
        }
    }

    public void GoBack()
    {
        if (!CanGoBack) return;
        if (_current is not null)
            _forwardStack.Push(_current);
        _current = _backStack.Pop();
        CurrentViewChanged?.Invoke(this, _current);
    }

    public void GoForward()
    {
        if (!CanGoForward) return;
        if (_current is not null)
            _backStack.Push(_current);
        _current = _forwardStack.Pop();
        CurrentViewChanged?.Invoke(this, _current);
    }

    public void ClearHistory()
    {
        _backStack.Clear();
        _forwardStack.Clear();
    }
}
