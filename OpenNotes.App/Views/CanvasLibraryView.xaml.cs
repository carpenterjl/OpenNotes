using System.Windows.Controls;
using System.Windows.Input;
using OpenNotes.Models;
using OpenNotes.ViewModels;

namespace OpenNotes.Views;

public partial class CanvasLibraryView : UserControl
{
    public CanvasLibraryView()
    {
        InitializeComponent();
    }

    private void CanvasCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border { DataContext: CanvasDocumentManifest manifest } &&
            DataContext is CanvasLibraryViewModel vm)
        {
            vm.OpenCanvasCommand.Execute(manifest);
        }
    }
}
