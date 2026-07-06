using System.Windows;
using System.Windows.Controls;
using OpenNotes.Models;
using OpenNotes.ViewModels;

namespace OpenNotes.Views;

public partial class KanbanView : UserControl
{
    public KanbanView() => InitializeComponent();

    private void TaskCard_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { Tag: TaskItem task } && DataContext is KanbanViewModel vm)
            vm.OpenTaskCommand?.Execute(task);
    }
}
