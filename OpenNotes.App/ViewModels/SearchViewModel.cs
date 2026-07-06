using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using OpenNotes.Interfaces;
using OpenNotes.Models;
using SearchQuery = OpenNotes.Interfaces.SearchQuery;

namespace OpenNotes.ViewModels;

public partial class SearchViewModel : ViewModelBase
{
    private readonly ISearchService _search;
    private readonly IWorkspaceService _workspaceService;
    private readonly INavigationService _navigation;

    [ObservableProperty] private string _query = string.Empty;
    [ObservableProperty] private bool _hasResults;

    public ObservableCollection<SearchResult> Results { get; } = [];

    public SearchViewModel(
        ISearchService search,
        IWorkspaceService workspaceService,
        INavigationService navigation)
    {
        _search = search;
        _workspaceService = workspaceService;
        _navigation = navigation;
        Title = "Search";
    }

    partial void OnQueryChanged(string value) => _ = SearchAsync();

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (string.IsNullOrWhiteSpace(Query))
        {
            Results.Clear();
            HasResults = false;
            return;
        }

        IsBusy = true;
        try
        {
            var sq = new SearchQuery
            {
                RawQuery = Query,
                MaxResults = 50,
                WorkspaceId = _workspaceService.ActiveWorkspace?.Id
            };
            var results = await _search.SearchAsync(sq);
            Results.Clear();
            foreach (var r in results.Take(50))
                Results.Add(r);
            HasResults = Results.Count > 0;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void OpenResult(SearchResult result)
    {
        if (result is null || _workspaceService.ActiveWorkspace is null) return;
        _navigation.NavigateTo<TaskEditorViewModel>(vm =>
        {
            _ = vm.LoadTaskAsync(_workspaceService.ActiveWorkspace.Id, result.TaskId);
        });
    }
}
