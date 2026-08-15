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
        Assert.Contains("https://mdassets/vendor/mermaid/mermaid.min.js", html);
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

    [Fact]
    public void Build_PrintCss_UsesPaginationRulesNotBlanketAvoid()
    {
        var html = _template.Build("<p>x</p>");

        // Page geometry must come solely from the print settings (no @page).
        Assert.DoesNotContain("@page", html);
        // No blanket keep-together on long blocks (caused large gaps).
        Assert.DoesNotContain("pre,blockquote { page-break-inside:avoid", html);
        // Even-distribution rules present.
        Assert.Contains("md-keep", html);
        Assert.Contains("orphans:3", html);
        Assert.Contains("break-after:avoid", html);
        Assert.Contains("break-inside:avoid", html);
        // Print body must not inherit the screen padding (it would stack on
        // top of the page margins).
        Assert.Contains("body { max-width:none; padding:0", html);
    }

    [Fact]
    public void Build_ExposesPrintReadyPromise()
    {
        var html = _template.Build("<p>x</p>");

        Assert.Contains("__mdPrintReady", html);
        Assert.Contains("__mdReadyJobs", html);
        Assert.Contains("mermaid.run().catch", html);
    }
}
