using MarkDesk.Services;

namespace MarkDesk.Tests;

public class ListContinuationTests
{
    [Theory]
    [InlineData("- item", "", "-")]
    [InlineData("* item", "", "*")]
    [InlineData("+ item", "", "+")]
    [InlineData("  - indented", "  ", "-")]
    [InlineData("1. first", "", "1.")]
    [InlineData("23) paren style", "", "23)")]
    [InlineData("\t- tabbed item", "\t", "-")]
    public void ParseItem_ReadsMarkers(string line, string indent, string marker)
    {
        var item = ListContinuation.ParseItem(line);
        Assert.NotNull(item);
        Assert.Equal(indent, item!.Indent);
        Assert.Equal(marker, item.Marker);
    }

    [Fact]
    public void ParseItem_TaskFlagIsDetachedFromContent()
    {
        var item = ListContinuation.ParseItem("- [ ] buy milk");
        Assert.NotNull(item);
        Assert.Equal("[ ] ", item.Task);
        Assert.Equal("buy milk", item.Content);

        var done = ListContinuation.ParseItem("- [X] done");
        Assert.NotNull(done);
        Assert.Equal("[ ] ", done.Task); // new line resets to unchecked
        Assert.Equal("done", done.Content);
    }

    [Theory]
    [InlineData("plain text")]
    [InlineData("# heading")]
    [InlineData("-nospace")]
    [InlineData("1.nospace")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("-- separator")] // second "-" makes it "- -"? no: marker '-' + space required
    public void ParseItem_RejectsNonListLines(string line) =>
        Assert.Null(ListContinuation.ParseItem(line));

    [Theory]
    [InlineData("1.", "2.")]
    [InlineData("9)", "10)")]
    [InlineData("123.", "124.")]
    [InlineData("-", "-")]
    [InlineData("*", "*")]
    public void NextMarker_IncrementsOrderedKeepsBullet(string marker, string expected) =>
        Assert.Equal(expected, ListContinuation.NextMarker(marker));

    [Fact]
    public void BuildNextLinePrefix_ContinuesOrderedTask()
    {
        var item = ListContinuation.ParseItem("  3. [x] done")!;
        Assert.Equal("  4. [ ] ", ListContinuation.BuildNextLinePrefix(item));
    }

    [Fact]
    public void BuildNextLinePrefix_ContinuesNestedBullet()
    {
        var item = ListContinuation.ParseItem("    - child")!;
        Assert.Equal("    - ", ListContinuation.BuildNextLinePrefix(item));
    }

    [Theory]
    [InlineData("- ")]
    [InlineData("-   ")]
    [InlineData("- [ ] ")]
    [InlineData("- [x] ")]
    [InlineData("1. ")]
    public void IsEmpty_DetectsExitCondition(string line) =>
        Assert.True(ListContinuation.IsEmpty(ListContinuation.ParseItem(line)!));

    [Theory]
    [InlineData("- content")]
    [InlineData("- [ ] task text")]
    [InlineData("1. text")]
    public void IsNonEmpty_ContinuesList(string line) =>
        Assert.False(ListContinuation.IsEmpty(ListContinuation.ParseItem(line)!));

    [Fact]
    public void IsInsideFence_TrueBetweenFences()
    {
        var lines = new[] { "text", "```csharp", "code here", "still code", "```", "after" };
        Assert.True(ListContinuation.IsInsideFence(lines, 2));
        Assert.True(ListContinuation.IsInsideFence(lines, 3));
    }

    [Fact]
    public void IsInsideFence_FalseOutsideFences()
    {
        var lines = new[] { "text", "```csharp", "code", "```", "after" };
        Assert.False(ListContinuation.IsInsideFence(lines, 0));
        Assert.False(ListContinuation.IsInsideFence(lines, 1));
        Assert.False(ListContinuation.IsInsideFence(lines, 4));
        Assert.False(ListContinuation.IsInsideFence(lines, 5));
    }

    [Fact]
    public void IsInsideFence_TracksTildeVsBacktickIndependently()
    {
        var lines = new[] { "~~~", "inner", "```", "still inside tilde", "~~~", "out" };
        // ``` inside a ~~~ block does not close it
        Assert.True(ListContinuation.IsInsideFence(lines, 3));
        Assert.False(ListContinuation.IsInsideFence(lines, 5));
    }

    [Fact]
    public void IsInsideFence_IndentedCodeLineIsNotAFence()
    {
        var lines = new[] { "    ```", "text" };
        Assert.False(ListContinuation.IsInsideFence(lines, 1));
    }
}
