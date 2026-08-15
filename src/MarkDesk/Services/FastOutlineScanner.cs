using System.IO;
using System.Text.RegularExpressions;

namespace MarkDesk.Services;

/// <summary>
/// Heading scan for large-file mode (tier 3): reads the document line by
/// line without building a Markdig syntax tree, so a >5 MB document yields
/// an outline in hundreds of milliseconds instead of tens of seconds.
///
/// Trade-offs vs <see cref="OutlineParser"/> (documented, intentional):
/// - text keeps its raw formatting markers (**bold**, `code`, …)
/// - empty headings (`#`) and Setext headings (`===` / `---`) are not found
/// - an ATX closing `#` (e.g. `# Title #`) is kept as literal text
/// </summary>
public static class FastOutlineScanner
{
    private static readonly Regex HeadingRegex = new(@"^\s{0,3}(#{1,6})\s+(.+)$", RegexOptions.Compiled);

    public static IReadOnlyList<OutlineItem> Extract(string markdown)
    {
        var result = new List<OutlineItem>();
        if (string.IsNullOrEmpty(markdown))
            return result;

        var inFence = false;
        var lineNumber = 0;
        using var reader = new StringReader(markdown);
        while (reader.ReadLine() is { } line)
        {
            lineNumber++;
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("```", StringComparison.Ordinal) ||
                trimmed.StartsWith("~~~", StringComparison.Ordinal))
            {
                inFence = !inFence;
                continue;
            }
            if (inFence)
                continue;

            var match = HeadingRegex.Match(line);
            if (match.Success)
                result.Add(new OutlineItem(match.Groups[1].Length, lineNumber, match.Groups[2].Value.Trim()));
        }
        return result;
    }
}