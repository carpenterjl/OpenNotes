using System.Windows.Controls;
using System.Windows.Input;

namespace OpenNotes.Views.Blocks;

public partial class ChecklistBlockView : UserControl
{
    public ChecklistBlockView() => InitializeComponent();

    // A TextBox generated inside a nested ItemsControl item does not reliably take
    // keyboard focus on click. Force it here so the item text becomes editable.
    private void ItemTextBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is TextBox box && !box.IsKeyboardFocusWithin)
        {
            // Force focus but let the event continue so the caret lands at the click point.
            box.Focus();
            Keyboard.Focus(box);
        }
    }
}
