using CommunityToolkit.Mvvm.ComponentModel;
using OpenNotes.Models.Blocks;

namespace OpenNotes.ViewModels.Blocks;

public partial class CodeBlockViewModel : BlockViewModelBase
{
    public static readonly IReadOnlyList<string> SupportedLanguages =
    [
        "csharp", "python", "javascript", "typescript", "java", "cpp", "c",
        "rust", "go", "sql", "xml", "json", "yaml", "bash", "powershell",
        "html", "css", "markdown", "plaintext"
    ];

    [ObservableProperty] private string _code;
    [ObservableProperty] private string _language;
    [ObservableProperty] private bool _showLineNumbers;

    public CodeBlockViewModel(CodeBlock block) : base(block)
    {
        _code = block.Code;
        _language = block.Language;
        _showLineNumbers = block.ShowLineNumbers;
    }

    partial void OnCodeChanged(string value) => RaiseContentChanged();
    partial void OnLanguageChanged(string value) => RaiseContentChanged();

    public override ContentBlock GetUpdatedBlock()
    {
        var b = (CodeBlock)Block;
        b.Code = Code;
        b.Language = Language;
        b.ShowLineNumbers = ShowLineNumbers;
        return Block;
    }
}
