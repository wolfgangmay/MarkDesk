using MarkDesk.Services;
using VerifyXunit;

namespace MarkDesk.Tests;

public class MarkdownRendererTests : VerifyBase
{
    private readonly MarkdownRenderer _renderer = new();

    public MarkdownRendererTests() : base() { }

    [Fact]
    public Task Basics_Headings_Inline()
        => Verify(_renderer.RenderToHtml("# Heading 1\n\nA paragraph with **bold**, *italic* and `code`."));

    [Fact]
    public Task Table_TaskList_Footnote()
        => Verify(_renderer.RenderToHtml(
            "| A | B |\n|---|---|\n| 1 | 2 |\n\n" +
            "- [x] done\n- [ ] todo\n\n" +
            "Has a note[^1].\n\n[^1]: the footnote text."));

    [Fact]
    public Task Fenced_Code_Block()
        => Verify(_renderer.RenderToHtml("```csharp\nvar x = 1;\n```\n"));

    [Fact]
    public Task Escapes_Raw_Html_NoPassthrough()
        => Verify(_renderer.RenderToHtml("<script>alert(1)</script>\n\n<b>not raw</b>"));

    [Fact]
    public Task Preserves_Math_Delimiters()
        => Verify(_renderer.RenderToHtml("Inline $a^2$ and block:\n\n$$E = mc^2$$\n"));

    [Fact]
    public void Emoji_RendersShortcode()
        => Assert.Contains("😄", _renderer.RenderToHtml("Hello :smile:"));

    [Fact]
    public void Heading_GeneratesGitHubId()
        => Assert.Contains("<h2 id=\"my-title\">", _renderer.RenderToHtml("## My Title"));

    [Fact]
    public void CustomContainer_RendersClassName()
        => Assert.Contains("<div class=\"warning\">", _renderer.RenderToHtml(":::warning\ncareful\n:::"));

    [Fact]
    public void GitHubAlert_EmitsBlockquoteWithMarker()
    {
        var html = _renderer.RenderToHtml("> [!NOTE]\n> useful info");
        Assert.Contains("<blockquote>", html);
        Assert.Contains("[!NOTE]", html);
    }
}
