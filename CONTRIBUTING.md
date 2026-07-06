# Contributing to OpenNotes

Thanks for your interest in contributing. This guide covers everything you need to get the project building locally, understand the architecture, and submit changes.

---

## Table of contents

- [Development setup](#development-setup)
- [Project structure](#project-structure)
- [Architecture primer](#architecture-primer)
- [How to add a content block type](#how-to-add-a-content-block-type)
- [How to add a new view](#how-to-add-a-new-view)
- [How to add an undo/redo command](#how-to-add-an-undoredo-command)
- [Code conventions](#code-conventions)
- [Testing](#testing)
- [Submitting a pull request](#submitting-a-pull-request)

---

## Development setup

**Prerequisites:**
- Windows 10 1809+ or Windows 11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/) (pre-installed on Windows 11)
- Any IDE with C# support — Visual Studio 2022, Rider, or VS Code with the C# extension

```powershell
git clone <repo-url>
cd OpenNotes
dotnet restore
dotnet build OpenNotes.sln
dotnet run --project OpenNotes.App
```

Run the test suite:

```powershell
dotnet test OpenNotes.Tests
```

All 246 tests should pass with 0 build errors and 0 warnings before you start.

---

## Project structure

```
OpenNotes.sln
├── OpenNotes.App/
│   ├── Models/             Pure data classes — no WPF dependencies
│   ├── Models/Blocks/      ContentBlock abstract hierarchy (one class per block type)
│   ├── Interfaces/         Service contracts (ITaskService, IThemeService, etc.)
│   ├── Services/           Concrete service implementations
│   ├── Persistence/        JSON repositories, autosave, backup
│   ├── ViewModels/         All ViewModels (no UI code)
│   ├── ViewModels/Blocks/  Per-block-type ViewModels wrapping ContentBlock models
│   ├── ViewModels/Canvas/  Canvas node/connector wrappers + BlockToNodeMapper
│   ├── Views/              XAML views + minimal code-behind
│   ├── Views/Blocks/       Per-block-type UserControls
│   ├── Themes/             Light, Dark, HighContrast, Custom ResourceDictionaries
│   ├── Styles/             Shared control styles (including MarkdownStyles.xaml)
│   ├── Converters/         IValueConverter implementations
│   ├── UndoRedo/           IUndoableCommand + UndoRedoService
│   ├── Export/             Markdown/HTML/ZIP/PDF exporters
│   ├── Dialogs/            Modal dialogs (Settings, CustomTheme, ColorPicker, etc.)
│   └── Infrastructure/     ServiceCollectionExtensions.cs (all DI registrations)
└── OpenNotes.Tests/        xUnit test project mirroring the app structure
```

The full developer reference — DI conventions, theme system, canvas format, startup flow, and a "Hard-Won Gotchas" section — is in [CLAUDE.md](CLAUDE.md). Read it before making changes to startup, DI registration, or block views.

---

## Architecture primer

**MVVM pattern:** Every view binds to a ViewModel. ViewModels extend `ViewModelBase` (which extends CommunityToolkit's `ObservableObject`). No business logic belongs in code-behind.

**Navigation:** `NavigationService.NavigateTo<TViewModel>()` resolves the VM from DI, calls `InitializeAsync()` on it, and raises `CurrentViewChanged`. Transient VMs are recreated on each navigation. Load data in `InitializeAsync`, never in the constructor.

**Dependency injection:** All registrations are in `Infrastructure/ServiceCollectionExtensions.cs`. Singletons hold app-wide state; transients are recreated per navigation. Do not create service instances manually — always resolve through DI.

**Messaging:** Cross-VM communication uses `WeakReferenceMessenger` from CommunityToolkit. Prefer this over direct VM references.

**Persistence:** `JsonPersistenceService.WriteAsync` is the only path to disk. It writes to a `.tmp` file and renames over the target — crash-safe on NTFS. All writes to the same path are serialized by a `SemaphoreSlim`.

---

## How to add a content block type

A content block is a unit of content inside a task (Markdown, LaTeX, code, etc.). Adding a new one requires touching several files — follow all steps in order.

**1. Define the model** in `Models/Blocks/ContentBlock.cs`:

```csharp
public class MyBlock : ContentBlock
{
    public string MyProp { get; set; } = "";
}
```

**2. Register it for JSON serialization** on the base class in the same file:

```csharp
[JsonDerivedType(typeof(MyBlock), "my_block")]
```

**3. Create the ViewModel** at `ViewModels/Blocks/MyBlockViewModel.cs`:

```csharp
public partial class MyBlockViewModel(MyBlock block) : BlockViewModelBase(block)
{
    [ObservableProperty] private string _myProp = block.MyProp;

    partial void OnMyPropChanged(string v) => RaiseContentChanged();

    public override ContentBlock GetUpdatedBlock()
    {
        ((MyBlock)Block).MyProp = MyProp;
        return Block;
    }
}
```

**4. Wire the factory** in `ViewModels/Blocks/BlockViewModelFactory.cs`:

```csharp
MyBlock b => new MyBlockViewModel(b),
```

**5. Create the view** at `Views/Blocks/MyBlockView.xaml` (a `UserControl` binding to `MyBlockViewModel`). Use `{DynamicResource}` for colors — never hardcoded brushes.

**6. Register the DataTemplate** in `Views/TaskEditorView.xaml` resources:

```xml
<DataTemplate DataType="{x:Type vm:MyBlockViewModel}">
    <blockViews:MyBlockView />
</DataTemplate>
```

**7. Add a creation command** in `TaskEditorViewModel`:

```csharp
[RelayCommand]
private void AddMyBlock() => AddNewBlock(new MyBlock());
```

**8. Add a button** in the `TaskEditorView` action bar.

> Important: `{StaticResource}` keys defined in `MainWindow.xaml` are not visible inside block UserControls (they have their own resource scope). Declare any converters you need in the global `Converters/Converters.xaml` (merged in `App.xaml`) and/or in `UserControl.Resources` locally. Use `{DynamicResource}` for theme brushes — a missing `{StaticResource}` throws `XamlParseException` at load while a missing `{DynamicResource}` is silent.

---

## How to add a new view

**1. Create the ViewModel** at `ViewModels/MyFeatureViewModel.cs` extending `ViewModelBase`. Load data in `InitializeAsync`, not the constructor.

**2. Create the view** at `Views/MyFeatureView.xaml` (`UserControl` binding to the ViewModel).

**3. Register a DataTemplate** in `MainWindow.xaml` resources:

```xml
<DataTemplate DataType="{x:Type vm:MyFeatureViewModel}">
    <views:MyFeatureView />
</DataTemplate>
```

**4. Register the ViewModel** in `Infrastructure/ServiceCollectionExtensions.cs`:

```csharp
services.AddTransient<MyFeatureViewModel>();
```

Choose `AddTransient` for views that are recreated on each navigation, `AddSingleton` for long-lived views that hold app-wide state (like `DashboardViewModel`). If you use singleton, subscribe to data events rather than reloading in `InitializeAsync`.

**5. Navigate to it:**

```csharp
_navigation.NavigateTo<MyFeatureViewModel>();
```

**6. Optionally** add a sidebar entry in `SidebarViewModel`/`SidebarView.xaml` and a command palette entry in `MainWindowViewModel.RegisterCommands()`.

---

## How to add an undo/redo command

Implement `IUndoableCommand`:

```csharp
public class MyCommand : IUndoableCommand
{
    public string Description => "Description shown in undo history";

    public void Execute()   { /* apply the change */ }
    public void Unexecute() { /* revert the change */ }
}
```

Push it to the stack — `Execute()` is called immediately:

```csharp
_undoRedo.Push(new MyCommand(...));
```

The `UndoRedoService` caps history at 200 entries (oldest dropped). Canvas commands live in `UndoRedo/Canvas/`.

---

## Code conventions

- **No UI code in ViewModels.** ViewModels must be testable without a WPF context.
- **No business logic in code-behind.** Views are for layout and event routing to commands only.
- **Data loads in `InitializeAsync`.** Never load data in constructors — the DI container resolves all services synchronously, and async work in a constructor is unsafe.
- **Write through to the model.** Every `OnXChanged` partial in a ViewModel must mirror the new value to its backing model property. Forgetting this means changes display correctly in-session but are lost on save/reload.
- **`NodeShape` is append-only.** `System.Text.Json` serializes this enum as an int — inserting a value mid-list corrupts existing persisted documents. Always append new values at the end.
- **Use `{DynamicResource}` for theme brushes,** never `{StaticResource}` or hardcoded colors.
- **No comments explaining what the code does.** Code should be self-documenting through naming. Only add a comment when the *why* is non-obvious: a hidden constraint, a workaround for a specific WPF quirk, or a subtle invariant that would surprise a reader.

---

## Testing

Tests live in `OpenNotes.Tests/`, mirroring the app structure.

```
OpenNotes.Tests/
├── Services/
├── ViewModels/
├── Persistence/
└── ...
```

**Conventions:**
- Test ViewModels and services in isolation — do not test WPF UI directly
- Use `NullLogger<T>.Instance` for logger dependencies
- Use the `WorkspaceRepository(persistence, logger, tempDir)` overload to isolate file I/O to a temp folder
- Mock service interfaces with `Moq`; do not mock data models or `BlockViewModelFactory`
- All async tests return `Task` (use `async Task`, not `async void`)
- The test project requires `<UseWPF>true</UseWPF>` to share model types — this is already set in the `.csproj`

The target before any PR is **0 new test failures**.

---

## Submitting a pull request

1. Fork the repo and create a branch from `main`
2. Make your changes — keep PRs focused on one thing
3. Ensure `dotnet build OpenNotes.sln` produces 0 errors and 0 warnings
4. Ensure `dotnet test OpenNotes.Tests` passes all tests
5. Describe what you changed and why in the PR description — "what" is in the diff, "why" is what matters
6. Reference any related issues

For larger changes (new features, architectural shifts), open an issue first to discuss the approach before writing code.
