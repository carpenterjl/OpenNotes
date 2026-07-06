using System.Linq;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using OpenNotes.ViewModels;
using OpenNotes.ViewModels.Blocks;

namespace OpenNotes.Views;

public partial class TaskEditorView : UserControl
{
    private const double MinBlockHeight = 40;

    public TaskEditorView()
    {
        InitializeComponent();
        InputBindings.Add(new KeyBinding(new RelayCommandBridge(() =>
        {
            if (DataContext is TaskEditorViewModel vm)
                _ = vm.SaveCommand.ExecuteAsync(null);
        }), Key.S, ModifierKeys.Control));
    }

    // Leaving the editor (navigation, workspace switch, app close): flush pending edits so
    // nothing is silently lost.
    private void View_Unloaded(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is TaskEditorViewModel vm)
            vm.OnViewUnloaded();
    }

    private void BlockResize_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (sender is not Thumb { Tag: BlockViewModelBase vm } thumb || thumb.Parent is not Grid grid)
            return;

        // When height is still auto (NaN), seed from the content's current rendered height.
        var current = double.IsNaN(vm.EditorHeight)
            ? grid.Children.OfType<ContentPresenter>().FirstOrDefault()?.ActualHeight ?? MinBlockHeight
            : vm.EditorHeight;

        vm.EditorHeight = System.Math.Max(MinBlockHeight, current + e.VerticalChange);
    }
}

file sealed class RelayCommandBridge(Action execute) : ICommand
{
    public event EventHandler? CanExecuteChanged { add { } remove { } }
    public bool CanExecute(object? parameter) => true;
    public void Execute(object? parameter) => execute();
}
