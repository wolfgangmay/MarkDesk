using Markdig;
using Markdig.Extensions.AutoIdentifiers;
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
        Markdown.ToHtml(markdown ?? string.Empty, Pipeline);

    /// <summary>
    /// Parses with the same pipeline as the preview so structural views
    /// (outline, reverse sync) never disagree with the rendered document.
    /// </summary>
    public MarkdownDocument Parse(string markdown) =>
        Markdown.Parse(markdown ?? string.Empty, Pipeline);
}
