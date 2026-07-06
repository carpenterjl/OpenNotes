using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using OpenNotes.ViewModels;

namespace OpenNotes.Views;

public partial class MainWindow : Window
{
    private readonly MainWindowViewModel _viewModel;

    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;
    }

    protected override async void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        await _viewModel.InitializeAsync();
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.Cleanup();
        base.OnClosed(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // Window-level fallback when focus isn't in the search box (e.g. a row button has focus).
        if (e.Key == Key.Escape && _viewModel.IsCommandPaletteOpen)
        {
            _viewModel.Palette.EscapeOrClose();
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }

    private void CommandPaletteOverlay_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not true) return;
        // Focus + select the search box once the overlay is laid out.
        Dispatcher.BeginInvoke(new Action(() =>
        {
            CommandSearchBox.Focus();
            CommandSearchBox.SelectAll();
        }), DispatcherPriority.Input);
    }

    private void CommandSearchBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var palette = _viewModel.Palette;
        switch (e.Key)
        {
            case Key.Tab:
                palette.AcceptTop();
                e.Handled = true;
                break;
            case Key.Enter:
                palette.Submit();
                e.Handled = true;
                break;
            case Key.Up:
                palette.MoveSelectionUp();
                e.Handled = true;
                break;
            case Key.Down:
                palette.MoveSelectionDown();
                e.Handled = true;
                break;
            case Key.Escape:
                palette.EscapeOrClose();
                e.Handled = true;
                break;
            case Key.Back:
                // Only step back a token when the current token box is empty.
                if (string.IsNullOrEmpty(CommandSearchBox.Text))
                {
                    palette.StepBack();
                    e.Handled = true;
                }
                break;
        }
    }
}
