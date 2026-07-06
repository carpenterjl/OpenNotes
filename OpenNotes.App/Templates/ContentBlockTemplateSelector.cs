using System.Windows;
using System.Windows.Controls;
using OpenNotes.ViewModels.Blocks;

namespace OpenNotes.Templates;

public class ContentBlockTemplateSelector : DataTemplateSelector
{
    public DataTemplate? TextTemplate { get; set; }
    public DataTemplate? MarkdownTemplate { get; set; }
    public DataTemplate? LatexTemplate { get; set; }
    public DataTemplate? MermaidTemplate { get; set; }
    public DataTemplate? CodeTemplate { get; set; }
    public DataTemplate? ImageTemplate { get; set; }
    public DataTemplate? ChecklistTemplate { get; set; }
    public DataTemplate? FallbackTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container)
    {
        return item switch
        {
            TextBlockViewModel => TextTemplate,
            MarkdownBlockViewModel => MarkdownTemplate,
            LatexBlockViewModel => LatexTemplate,
            MermaidBlockViewModel => MermaidTemplate,
            CodeBlockViewModel => CodeTemplate,
            ImageBlockViewModel => ImageTemplate,
            ChecklistBlockViewModel => ChecklistTemplate,
            _ => FallbackTemplate
        };
    }
}
