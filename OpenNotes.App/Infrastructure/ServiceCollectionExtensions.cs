using Microsoft.Extensions.DependencyInjection;
using OpenNotes.Export;
using OpenNotes.Interfaces;
using OpenNotes.Notifications;
using OpenNotes.Persistence;
using OpenNotes.Services;
using OpenNotes.UndoRedo;
using OpenNotes.ViewModels;
using OpenNotes.ViewModels.Blocks;
using OpenNotes.Views;

namespace OpenNotes.Infrastructure;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddOpenNotesServices(this IServiceCollection services)
    {
        // Persistence
        services.AddSingleton<IPersistenceService, JsonPersistenceService>();
        services.AddSingleton<IAppSettingsService, AppSettingsService>();
        services.AddSingleton<WorkspaceRepository>();
        services.AddSingleton<TaskRepository>();
        services.AddSingleton<DiagramRepository>();
        services.AddSingleton<CanvasDocumentRepository>();
        services.AddSingleton<IBackupService, BackupService>();

        // Autosave (also IHostedService)
        services.AddSingleton<AutosaveService>();
        services.AddSingleton<IAutosaveService>(sp => sp.GetRequiredService<AutosaveService>());
        services.AddHostedService(sp => sp.GetRequiredService<AutosaveService>());

        // Domain services
        services.AddSingleton<IWorkspaceService, WorkspaceService>();
        services.AddSingleton<ITaskService, TaskService>();
        services.AddSingleton<ICanvasDocumentService, CanvasDocumentService>();
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<ICommandPaletteService, CommandPaletteService>();
        services.AddSingleton<IUndoRedoService, UndoRedoService>();

        // Search (stub for phase 1)
        services.AddSingleton<ISearchService, Search.SearchService>();

        // Notifications
        services.AddSingleton<INotificationService, NotificationService>();
        services.AddHostedService<ReminderScheduler>();

        // Export
        services.AddSingleton<IExportService, ExportService>();
        services.AddSingleton<IMermaidSvgExporter, MermaidSvgExporter>();
        services.AddSingleton<ICanvasPdfExporter, CanvasPdfExporter>();

        // Canvas per-document color overrides (runtime resources)
        services.AddSingleton<ICanvasThemeService, CanvasThemeService>();

        // Block factory
        services.AddSingleton<BlockViewModelFactory>();

        // ViewModels
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<SidebarViewModel>();
        services.AddSingleton<DashboardViewModel>();
        services.AddTransient<TaskListViewModel>();
        services.AddTransient<TaskEditorViewModel>();
        services.AddTransient<KanbanViewModel>();
        services.AddTransient<CanvasEditorViewModel>();
        services.AddTransient<CanvasLibraryViewModel>();
        services.AddTransient<SearchViewModel>();
        services.AddTransient<CommandPaletteViewModel>();

        // Views
        services.AddSingleton<MainWindow>();

        return services;
    }
}
