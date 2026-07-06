using System.Windows;
using System.Windows.Controls;
using OpenNotes.Models;
using OpenNotes.ViewModels;

namespace OpenNotes.Views;

public partial class TaskListView : UserControl
{
    public TaskListView() => InitializeComponent();

    private void TaskItem_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: TaskItem task } && DataContext is TaskListViewModel vm)
            vm.OpenTaskCommand?.Execute(task);
    }
}
