using CommunityToolkit.Mvvm.ComponentModel;
using OpenNotes.Models.Blocks;
using OpenNotes.Services;
using WpfMath.Parsers;

namespace OpenNotes.ViewModels.Blocks;

public partial class LatexBlockViewModel : BlockViewModelBase
{
    [ObservableProperty] private string _formula;
    [ObservableProperty] private bool _displayMode;
    [ObservableProperty] private bool _isPreviewMode = true;
    [ObservableProperty] private string? _errorMessage;

    /// <summary>The last successfully-parsed formula. The preview control binds to this
    /// (not the raw <see cref="Formula"/>) so invalid/mid-edit input never breaks the render.</summary>
    [ObservableProperty] private string _renderedFormula;

    public LatexBlockViewModel(LatexBlock block) : base(block)
    {
        _formula = block.Formula;
        _displayMode = block.DisplayMode;
        _renderedFormula = block.Formula;
        Validate(block.Formula);
    }

    partial void OnFormulaChanged(string value)
    {
        Validate(value);
        RaiseContentChanged();
    }

    private void Validate(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            ErrorMessage = null;
            RenderedFormula = string.Empty;
            return;
        }

        try
        {
            // Normalize first: strips % comments and $ delimiters, maps unsupported commands
            // (\mathbf, \dfrac, amsmath environments, …) onto WpfMath equivalents, and turns
            // hard newlines into \\ so multi-line input renders as stacked lines.
            var normalized = LatexPreprocessor.Normalize(value);
            WpfTeXFormulaParser.Instance.Parse(normalized, null);
            RenderedFormula = normalized;
            ErrorMessage = null;
        }
        catch (Exception ex)
        {
            // Keep the last good render; surface the parse error to the user.
            ErrorMessage = ex.Message;
        }
    }

    public override ContentBlock GetUpdatedBlock()
    {
        var b = (LatexBlock)Block;
        b.Formula = Formula;
        b.DisplayMode = DisplayMode;
        return Block;
    }
}
