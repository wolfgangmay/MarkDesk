using Markdig.Syntax;

namespace MarkDesk.Services;

public interface IMarkdownRenderer
{
    string RenderToHtml(string markdown);

    string RenderToHtml(string markdown, CancellationToken token);

    /// <summary>Renders an already-parsed document (see <see cref="Parse"/>).</summary>
    string RenderToHtml(Markdig.Syntax.MarkdownDocument document, CancellationToken token);

    /// <summary>Syntax tree parsed with the same pipeline as the preview.</summary>
    MarkdownDocument Parse(string markdown);
}
