using MarkDesk.Services;

namespace MarkDesk.Tests;

public class MarkdownWrappingTests
{
    [Theory]
    [InlineData("text", "**", "**text**")]
    [InlineData("text", "`", "`text`")]
    [InlineData("", "*", "**")]
    public void Wrap_AddsTokenOnBothSides(string text, string token, string expected) =>
        Assert.Equal(expected, MarkdownWrapping.Wrap(text, token));

    [Theory]
    [InlineData("**text**", "**", "text")]
    [InlineData("`code`", "`", "code")]
    [InlineData("*text*", "*", "text")]
    [InlineData("text", "**", "text")]      // not wrapped -> unchanged
    [InlineData("*text", "*", "*text")]     // one-sided -> unchanged
    [InlineData("****", "**", "")]           // empty payload
    public void Unwrap_StripsTokenWhenPresent(string text, string token, string expected) =>
        Assert.Equal(expected, MarkdownWrapping.Unwrap(text, token));

    [Theory]
    [InlineData("text", "**", "**text**")]
    [InlineData("**text**", "**", "text")]
    [InlineData("*text*", "*", "text")]
    public void Toggle_SwitchesDirection(string text, string token, string expected) =>
        Assert.Equal(expected, MarkdownWrapping.Toggle(text, token));

    [Theory]
    [InlineData("label", "[label]()")]
    [InlineData("", "[]()")]
    public void ToggleLink_PlainTextBecomesLinkWithEmptyUrl(string text, string expected) =>
        Assert.Equal(expected, MarkdownWrapping.ToggleLink(text));

    [Fact]
    public void ToggleLink_LinkBecomesLabel()
    {
        Assert.Equal("label", MarkdownWrapping.ToggleLink("[label](http://x)"));
        Assert.Equal("label", MarkdownWrapping.ToggleLink("[label]()"));
    }

    [Fact]
    public void ToggleLink_UnclosedBracketTextIsWrappedLikePlainText() =>
        Assert.Equal("[[not a link]()", MarkdownWrapping.ToggleLink("[not a link"));

    [Theory]
    [InlineData('`', true)]
    [InlineData('[', true)]
    [InlineData('(', true)]
    [InlineData(']', false)]
    [InlineData('*', false)]
    [InlineData('"', false)] // quotes deliberately NOT paired (CJK input)
    public void IsPairedChar_OnlyBacktickBracketParen(char c, bool expected) =>
        Assert.Equal(expected, MarkdownWrapping.IsPairedChar(c));

    [Theory]
    [InlineData('`', '`')]
    [InlineData('[', ']')]
    [InlineData('(', ')')]
    public void ClosingFor_MapsOpeningToClosing(char open, char close) =>
        Assert.Equal(close, MarkdownWrapping.ClosingFor(open));
}
