using System.Windows;
using System.Windows.Input;

namespace OpenNotes.Dialogs;

/// <summary>Multiline code editor dialog with a syntax-language picker,
/// used by canvas code nodes (the task editor has its own inline AvalonEdit).</summary>
public partial class CodeInputDialog : Window
{
    public string CodeValue => InputTextBox.Text;
    public string LanguageValue => LanguageCombo.SelectedItem as string ?? "plaintext";

    public CodeInputDialog(string title, string code, string language, IReadOnlyList<string> languages)
    {
        InitializeComponent();
        Title = title;
        InputTextBox.Text = code;
        LanguageCombo.ItemsSource = languages;
        LanguageCombo.SelectedItem = languages.Contains(language) ? language : "plaintext";
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
