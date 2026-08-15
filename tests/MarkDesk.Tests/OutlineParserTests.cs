using MarkDesk.Services;

namespace MarkDesk.Tests;

public class OutlineParserTests
{
    private static IReadOnlyList<OutlineItem> Parse(string markdown) =>
        OutlineParser.Extract(new MarkdownRenderer().Parse(markdown));

    [Fact]
    public void Parses_LevelsAndOneBasedLines()
    {
        var items = Parse("# Title\n\nparagraph\n\n## Section A\n\ntext\n\n### Deep\n");

        Assert.Equal(3, items.Count);
        Assert.Equal((1, 1, "Title"), (items[0].Level, items[0].Line, items[0].Text));
        Assert.Equal((2, 5, "Section A"), (items[1].Level, items[1].Line, items[1].Text));
        Assert.Equal((3, 9, "Deep"), (items[2].Level, items[2].Line, items[2].Text));
    }

    [Fact]
    public void Skips_HeadingsInsideFencedCodeBlocks()
    {
        var md = "# Real\n\n```csharp\n// ## Fake\n```\n\n## After\n";
        var items = Parse(md);

        Assert.Equal(2, items.Count);
        Assert.Equal("Real", items[0].Text);
        Assert.Equal("After", items[1].Text);
    }

    [Fact]
    public void Skips_IndentedCodeHeading()
    {
        var items = Parse("# Real\n\n    ## Indented code\n");
        Assert.Single(items);
        Assert.Equal("Real", items[0].Text);
    }

    [Fact]
    public void Extracts_InnerFormattingAsPlainText()
    {
        var items = Parse("## **bold** and `code` and [link](url)\n");
        Assert.Single(items);
        Assert.Equal("bold and code and link", items[0].Text);
    }

    [Fact]
    public void Supports_SetextStyleHeadings()
    {
        var items = Parse("Title\n=====\n\nSub\n---\n");
        Assert.Equal(2, items.Count);
        Assert.Equal(1, items[0].Level);
        Assert.Equal(2, items[1].Level);
    }

    [Fact]
    public void EmptyHeading_GetsPlaceholder()
    {
        var items = Parse("##\n");
        Assert.Single(items);
        Assert.Equal("(empty)", items[0].Text);
    }

    [Fact]
    public void EmptyDocument_YieldsNoHeadings()
    {
        Assert.Empty(Parse(""));
        Assert.Empty(Parse("just text, no headings\n"));
    }

    [Fact]
    public void SamePipeline_AsPreview_IndentedFourSpacesIsNotAHeading()
    {
        // The outline must agree with the preview: 4-space indented `#` is a
        // code block there, so it must not appear in the outline either.
        var items = Parse("# ok\n\n    # indented\n");
        Assert.Single(items);
    }
}
