using System.Windows;
using System.Windows.Controls;
using OpenNotes.Models;
using OpenNotes.ViewModels;

namespace OpenNotes.Views;

public partial class SearchView : UserControl
{
    public SearchView()
    {
        InitializeComponent();
        Loaded += (_, _) => SearchBox.Focus();
    }

    private void Result_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: SearchResult result } && DataContext is SearchViewModel vm)
            vm.OpenResultCommand?.Execute(result);
    }
}
