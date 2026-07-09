using CommunityToolkit.Mvvm.ComponentModel;
using OpenNotes.Models.Blocks;

namespace OpenNotes.ViewModels.Blocks;

/// <summary>ViewModel for the LaTeX block. Rendering and validation now live in the KaTeX/WebView2
/// preview (<c>LatexBlockView</c>): the page's <c>window.renderLatex</c> reports success/failure
/// through the postMessage channel, which sets/clears <see cref="ErrorMessage"/> here, and the
/// last good render survives invalid intermediate input inside the page DOM (the old WpfMath
/// parse + RenderedFormula last-good mechanism is gone).</summary>
public partial class LatexBlockViewModel : BlockViewModelBase
{
    [ObservableProperty] private string _formula;
    [ObservableProperty] private bool _displayMode;
    [ObservableProperty] private bool _isPreviewMode = true;
    [ObservableProperty] private string? _errorMessage;

    public LatexBlockViewModel(LatexBlock block) : base(block)
    {
        _formula = block.Formula;
        _displayMode = block.DisplayMode;
    }

    partial void OnFormulaChanged(string value) => RaiseContentChanged();

    public override ContentBlock GetUpdatedBlock()
    {
        var b = (LatexBlock)Block;
        b.Formula = Formula;
        b.DisplayMode = DisplayMode;
        return Block;
    }
}
