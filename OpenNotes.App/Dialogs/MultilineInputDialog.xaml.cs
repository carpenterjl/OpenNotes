using System.Windows;
using System.Windows.Input;

namespace OpenNotes.Dialogs;

public partial class MultilineInputDialog : Window
{
    public string InputValue => InputTextBox.Text;

    public MultilineInputDialog(string title, string prompt, string defaultValue = "")
    {
        InitializeComponent();
        Title = title;
        PromptText.Text = prompt;
        InputTextBox.Text = defaultValue;
        Loaded += (_, _) => { InputTextBox.Focus(); InputTextBox.CaretIndex = InputTextBox.Text.Length; };
    }

    private void OK_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void InputTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        // Enter inserts a newline (multiline); Ctrl+Enter accepts, Escape cancels.
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            DialogResult = true;
            Close();
        }
        else if (e.Key == Key.Escape)
        {
            DialogResult = false;
            Close();
        }
    }
}
