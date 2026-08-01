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
}
