namespace MarkDesk.Services;

public interface IMarkdownRenderer
{
    string RenderToHtml(string markdown);
}
