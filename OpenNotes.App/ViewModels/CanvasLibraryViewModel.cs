using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using OpenNotes.Interfaces;
using OpenNotes.Models;

namespace OpenNotes.ViewModels;

/// <summary>Library of the workspace's standalone canvas documents (canvases not owned by a task).
/// Task-owned canvases are opened from their task's editor instead and are not listed here.</summary>
public partial class CanvasLibraryViewModel : ViewModelBase
{
    private readonly IWorkspaceService _workspaceService;
    private readonly ICanvasDocumentService _documentService;
    private readonly INavigationService _navigation;
    private readonly IDialogService _dialogs;
    private readonly ILogger<CanvasLibraryViewModel> _logger;

    public ObservableCollection<CanvasDocumentManifest> Canvases { get; } = [];

    public CanvasLibraryViewModel(
        IWorkspaceService workspaceService,
        ICanvasDocumentService documentService,
        INavigationService navigation,
        IDialogService dialogs,
        ILogger<CanvasLibraryViewModel> logger)
    {
        _workspaceService = workspaceService;
        _documentService = documentService;
        _navigation = navigation;
        _dialogs = dialogs;
        _logger = logger;
        Title = "Canvases";
    }

    public override async Task InitializeAsync(CancellationToken ct = default)
    {
        var workspace = _workspaceService.ActiveWorkspace;
        if (workspace is null) return;

        IsBusy = true;
        try
        {
            var manifests = await _documentService.ListStandaloneAsync(workspace.Id, ct);
            Canvases.Clear();
            foreach (var m in manifests)
                Canvases.Add(m);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to list standalone canvases");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task CreateCanvasAsync()
    {
        var workspace = _workspaceService.ActiveWorkspace;
        if (workspace is null) return;

        var name = await _dialogs.ShowInputAsync("New Canvas", "Canvas name:", "New Canvas");
        if (string.IsNullOrWhiteSpace(name)) return;

        var doc = await _documentService.CreateStandaloneAsync(workspace.Id, name);
        OpenCanvas(doc.Manifest);
    }

    [RelayCommand]
    private void OpenCanvas(CanvasDocumentManifest? manifest)
    {
        var workspace = _workspaceService.ActiveWorkspace;
        if (manifest is null || workspace is null) return;
        var wsId = workspace.Id;
        _navigation.NavigateTo<CanvasEditorViewModel>(vm => _ = vm.LoadStandaloneAsync(wsId, manifest.Id));
    }

    [RelayCommand]
    private async Task DeleteCanvasAsync(CanvasDocumentManifest? manifest)
    {
        var workspace = _workspaceService.ActiveWorkspace;
        if (manifest is null || workspace is null) return;

        var confirm = await _dialogs.ShowConfirmAsync(
            "Delete Canvas",
            $"Permanently delete '{manifest.Title}' and all its pages?",
            "Delete", "Cancel");
        if (!confirm) return;

        await _documentService.DeleteAsync(workspace.Id, manifest.Id);
        Canvases.Remove(manifest);
    }
}
