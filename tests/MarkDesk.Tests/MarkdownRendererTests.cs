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
    {
        var html = _renderer.RenderToHtml("## My Title");
        Assert.Contains("<h2 ", html);
        Assert.Contains("id=\"my-title\"", html);
    }

    [Fact]
    public void RenderToHtml_WithCancelledToken_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.ThrowsAny<OperationCanceledException>(
            () => _renderer.RenderToHtml("# Title\n\nBody\n", cts.Token));
    }

    [Fact]
    public void Blocks_CarryOneBasedSourceLine()
    {
        var html = _renderer.RenderToHtml("first para\n\nsecond para\n");

        Assert.Contains("data-line=\"1\"", html);
        Assert.Contains("data-line=\"3\"", html);
    }

    [Fact]
    public void FencedCode_CarriesOpeningLine()
    {
        var html = _renderer.RenderToHtml("text\n\n```csharp\ncode\n```\n");

        Assert.Contains("data-line=\"3\"", html);
    }

    [Fact]
    public void Heading_CarriesLineAlongsideAutoId()
    {
        var html = _renderer.RenderToHtml("# Title\n");

        Assert.Contains("<h1 ", html);
        Assert.Contains("id=\"title\"", html);
        Assert.Contains("data-line=\"1\"", html);
    }

    [Fact]
    public void CustomContainer_RendersClassName()
        => Assert.Contains("class=\"warning\"", _renderer.RenderToHtml(":::warning\ncareful\n:::"));

    [Fact]
    public void GitHubAlert_EmitsBlockquoteWithMarker()
    {
        var html = _renderer.RenderToHtml("> [!NOTE]\n> useful info");
        Assert.Contains("blockquote", html);
        Assert.Contains("[!NOTE]", html);
    }

    // The preview's anchor-click JS resolves links via
    // decodeURIComponent(href fragment) -> getElementById(...). This locks the
    // renderer contract it depends on: for every heading, the percent-decoded
    // fragment of a matching anchor link equals the heading's generated id.
    [Theory]
    [InlineData("## My Title")]
    [InlineData("## 中文标题")]
    [InlineData("## 中文 标题")]
    [InlineData("## 🚀 Heading")]
    [InlineData("## C++ & C# Specials!")]
    public void AnchorLink_HrefDecodesToHeadingId(string heading)
    {
        var id = ExtractMatch(_renderer.RenderToHtml(heading), "<h2 id=\"([^\"]*)\"");
        var html = _renderer.RenderToHtml($"[t](#{id})\n\n{heading}\n");
        var fragment = ExtractMatch(html, "href=\"#([^\"]*)\"");
        Assert.Equal(id, Uri.UnescapeDataString(fragment));
    }

    private static string ExtractMatch(string html, string pattern) =>
        System.Text.RegularExpressions.Regex.Match(html, pattern).Groups[1].Value;
}
