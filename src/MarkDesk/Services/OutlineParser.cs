using System.Text;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace MarkDesk.Services;

public sealed record OutlineItem(int Level, int Line, string Text);

/// <summary>
/// Extracts the heading outline from a Markdig document (parsed with the
/// preview pipeline, so the outline is exactly what the preview renders as
/// headings — including its quirks, e.g. `---` under a paragraph being an
/// h2, or a YAML front-matter line rendering as one).
/// </summary>
public static class OutlineParser
{
    private const string EmptyPlaceholder = "(empty)";

    public static IReadOnlyList<OutlineItem> Extract(MarkdownDocument document)
    {
        var result = new List<OutlineItem>();
        foreach (var heading in document.Descendants<HeadingBlock>())
        {
            var text = ExtractText(heading);
            result.Add(new OutlineItem(heading.Level, heading.Line + 1, text));
        }
        return result;
    }

    private static string ExtractText(HeadingBlock heading)
    {
        if (heading.Inline == null)
            return EmptyPlaceholder;
        var sb = new StringBuilder();
        foreach (var inline in heading.Inline.Descendants())
        {
            switch (inline)
            {
                case LiteralInline lit:
                    sb.Append(lit.Content.ToString());
                    break;
                case CodeInline code:
                    sb.Append(code.Content);
                    break;
                case AutolinkInline auto:
                    sb.Append(auto.Url);
                    break;
                default:
                    // EmojiInline (namespace moved between Markdig versions) —
                    // resolve its Content dynamically to avoid a hard reference.
                    if (inline is LeafInline && inline.GetType().Name == "EmojiInline")
                        sb.Append((string)((dynamic)inline).Content);
                    break;
            }
        }
        var text = sb.ToString().Trim();
        return text.Length == 0 ? EmptyPlaceholder : text;
    }
}
