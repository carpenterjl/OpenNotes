using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using OpenNotes.Models;
using OpenNotes.ViewModels;

namespace OpenNotes.Views;

public partial class DashboardView : UserControl
{
    public DashboardView()
    {
        InitializeComponent();
    }

    private void TaskRow_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: TaskItem task } && DataContext is DashboardViewModel vm)
            vm.OpenTaskCommand.Execute(task);
    }

    private void StatCard_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string category } && DataContext is DashboardViewModel vm)
            vm.ShowCategoryCommand.Execute(category);
    }
}
