using System.Windows;
using System.Windows.Input;

namespace OpenNotes.Dialogs;

public partial class InputDialog : Window
{
    public string InputValue => InputTextBox.Text;

    public InputDialog(string title, string prompt, string defaultValue = "")
    {
        InitializeComponent();
        Title = title;
        PromptText.Text = prompt;
        InputTextBox.Text = defaultValue;
        InputTextBox.SelectAll();
        Loaded += (_, _) => InputTextBox.Focus();
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
        if (e.Key == Key.Enter) { DialogResult = true; Close(); }
        if (e.Key == Key.Escape) { DialogResult = false; Close(); }
    }
}
