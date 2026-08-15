using Markdig.Syntax;

namespace MarkDesk.Services;

public interface IMarkdownRenderer
{
    string RenderToHtml(string markdown);

    /// <summary>Syntax tree parsed with the same pipeline as the preview.</summary>
    MarkdownDocument Parse(string markdown);
}
