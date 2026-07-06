using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using OpenNotes.Services;
using OpenNotes.ViewModels.Blocks;

namespace OpenNotes.Views.Blocks;

public partial class CodeBlockView : UserControl
{
    private string _language = "plaintext";

    public CodeBlockView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        // Theme switches re-brush the DynamicResource surfaces automatically, but syntax token
        // colors live as plain values inside the (process-global) highlighting definitions and
        // need a re-apply. The root Border's Background flipping to the new theme brush is the
        // observable signal that a theme change happened.
        Loaded += (_, _) => ThemeSignal(add: true);
        Unloaded += (_, _) => ThemeSignal(add: false);
    }

    private void ThemeSignal(bool add)
    {
        if (Content is not Border border) return;
        var dpd = System.ComponentModel.DependencyPropertyDescriptor.FromProperty(
            Border.BackgroundProperty, typeof(Border));
        if (add) dpd.AddValueChanged(border, OnThemeSurfaceChanged);
        else dpd.RemoveValueChanged(border, OnThemeSurfaceChanged);
    }

    private void OnThemeSurfaceChanged(object? sender, EventArgs e) => ApplySyntaxHighlighting(_language);

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not CodeBlockViewModel vm) return;

        // Two-way bind Code via events (AvalonEdit doesn't support binding natively)
        Editor.Text = vm.Code;
        Editor.TextChanged += (_, _) =>
        {
            vm.Code = Editor.Text;
        };

        vm.PropertyChanged += (_, pe) =>
        {
            if (pe.PropertyName == nameof(vm.Code) && Editor.Text != vm.Code)
                Editor.Text = vm.Code;
            if (pe.PropertyName == nameof(vm.Language))
                ApplySyntaxHighlighting(vm.Language);
        };

        ApplySyntaxHighlighting(vm.Language);
    }

    private void ApplySyntaxHighlighting(string language)
    {
        _language = language;
        var def = CodeHighlighting.GetDefinition(language);
        CodeHighlighting.ApplyTheme(def, CodeHighlighting.IsDarkTheme());
        Editor.SyntaxHighlighting = null; // force a re-colorize even when the definition is unchanged
        Editor.SyntaxHighlighting = def;
    }
}
