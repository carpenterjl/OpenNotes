using OpenNotes.Interfaces;
using OpenNotes.Models.Blocks;

namespace OpenNotes.ViewModels.Blocks;

public class BlockViewModelFactory
{
    private readonly IDialogService _dialogs;

    public BlockViewModelFactory(IDialogService dialogs)
    {
        _dialogs = dialogs;
    }

    public BlockViewModelBase Create(ContentBlock block) => block switch
    {
        TextBlock b => new TextBlockViewModel(b),
        MarkdownBlock b => new MarkdownBlockViewModel(b),
        LatexBlock b => new LatexBlockViewModel(b),
        MermaidBlock b => new MermaidBlockViewModel(b),
        CodeBlock b => new CodeBlockViewModel(b),
        ImageBlock b => new ImageBlockViewModel(b, _dialogs),
        ChecklistBlock b => new ChecklistBlockViewModel(b),
        _ => new TextBlockViewModel(new TextBlock { Content = block.GetType().Name })
    };
}
