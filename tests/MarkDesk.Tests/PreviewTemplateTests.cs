using MarkDesk.Services;

namespace MarkDesk.Tests;

public class PreviewTemplateTests
{
    private readonly PreviewTemplate _template = new();

    [Fact]
    public void Build_References_LocalAssets_NotCdn()
    {
        var html = _template.Build("<p>body</p>");

        Assert.Contains("https://mdassets/vendor/highlight/highlight.min.js", html);
        Assert.Contains("https://mdassets/vendor/katex/katex.min.css", html);
        Assert.Contains("https://mdassets/vendor/katex/auto-render.min.js", html);
        Assert.DoesNotContain("cdnjs.cloudflare.com", html);
        Assert.DoesNotContain("cdn.jsdelivr.net", html);
    }

    [Fact]
    public void Build_Dark_UsesDarkHighlightTheme()
    {
        var light = _template.Build("<p>x</p>");
        var dark = _template.Build("<p>x</p>", dark: true);

        Assert.Contains("github.min.css", light);
        Assert.Contains("github-dark.min.css", dark);
    }

    [Fact]
    public void Build_WiresBodyHtml_IntoDocument()
    {
        var html = _template.Build("<h1 id=\"t\">Title</h1>");

        Assert.Contains("<h1 id=\"t\">Title</h1>", html);
    }
}
