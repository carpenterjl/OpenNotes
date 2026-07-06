using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using OpenNotes.Models;
using OpenNotes.ViewModels;

namespace OpenNotes.Views;

public partial class SidebarView : UserControl
{
    public SidebarView()
    {
        InitializeComponent();
    }

    private void WorkspaceItem_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem item && item.DataContext is WorkspaceMetadata ws &&
            DataContext is SidebarViewModel vm)
        {
            vm.SelectWorkspaceCommand.Execute(ws);
        }
    }
}
