using System.Globalization;
using Markdig;
using Markdig.Extensions.AutoIdentifiers;
using Markdig.Renderers.Html;
using Markdig.Syntax;

namespace MarkDesk.Services;

public sealed class MarkdownRenderer : IMarkdownRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()        // GFM tables
        .UseTaskLists()         // - [ ] task lists
        .UseFootnotes()         // [^1]
        .UseEmphasisExtras()    // strikethrough, subscript, superscript
        .UseAutoLinks()         // bare URLs
        .UseGridTables()
        .UseGenericAttributes()
        .UseEmojiAndSmiley()              // :smile: -> 😄
        .UseAutoIdentifiers(AutoIdentifierOptions.GitHub)  // heading -> id (anchors)
        .UseCustomContainers()            // :::warning ... :::
        .DisableHtml()          // no raw HTML passthrough (§4.2 XSS guard)
        .Build();

    public string RenderToHtml(string markdown) =>
        RenderWithSourceLines(markdown ?? string.Empty, CancellationToken.None);

    /// <summary>
    /// Cancellable variant for the background render pipeline. Cancellation
    /// is coarse (checked between Parse and ToHtml): a 5 MB document parses
    /// in ~7 s during which the token is not observed, but once parsing is
    /// done a cancelled render exits before producing any HTML.
    /// </summary>
    public string RenderToHtml(string markdown, CancellationToken token) =>
        RenderWithSourceLines(markdown ?? string.Empty, token);

    /// <summary>Syntax tree parsed with the same pipeline as the preview.</summary>
    public MarkdownDocument Parse(string markdown) =>
        Markdown.Parse(markdown ?? string.Empty, Pipeline);

    /// <summary>
    /// Renders HTML where every block carries its 1-based source line as a
    /// `data-line` attribute — the preview's reverse-sync (click a rendered
    /// block to jump to its source) resolves clicks against it. Uses the
    /// public attribute mechanism, so core block renderers emit it natively.
    /// </summary>
    private static string RenderWithSourceLines(string markdown, CancellationToken token)
    {
        var document = Markdown.Parse(markdown, Pipeline);
        token.ThrowIfCancellationRequested();
        foreach (var block in document.Descendants<Block>())
        {
            var attrs = block.GetAttributes();
            attrs.Properties ??= new List<KeyValuePair<string, string?>>();
            if (!attrs.Properties.Any(p => p.Key == "data-line"))
                attrs.Properties.Add(new KeyValuePair<string, string?>("data-line",
                    (block.Line + 1).ToString(CultureInfo.InvariantCulture)));
        }
        token.ThrowIfCancellationRequested();
        return document.ToHtml(Pipeline);
    }
}
