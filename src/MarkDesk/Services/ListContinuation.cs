using System.Text.RegularExpressions;

namespace MarkDesk.Services;

/// <summary>A parsed Markdown list item line (bullet, ordered, or task).</summary>
public sealed record ListItemInfo(string Indent, string Marker, string Task, string Content);

/// <summary>
/// Pure functions behind the smart list-continuation typing assist
/// (Enter continues `-` / `1.` / `- [ ]` items). No editor dependencies so
/// the rules are directly unit-testable.
/// </summary>
public static class ListContinuation
{
    private static readonly Regex ItemPattern = new(
        @"^(?<indent>[ \t]*)(?<marker>[-*+]|\d+[.)])(?<space>[ \t]+)(?<task>\[[ xX]\][ \t]+)?(?<content>.*)$",
        RegexOptions.Compiled);

    /// <summary>Opens/closes a fenced code block (``` or ~~~).</summary>
    private static readonly Regex FencePattern =
        new(@"^[ \t]{0,3}(?<fence>`{3,}|~{3,})", RegexOptions.Compiled);

    public static ListItemInfo? ParseItem(string line) =>
        ItemPattern.Match(line) is { Success: true } m
            ? new ListItemInfo(
                m.Groups["indent"].Value,
                m.Groups["marker"].Value,
                m.Groups["task"].Success ? "[ ] " : string.Empty,
                m.Groups["content"].Value)
            : null;

    /// <summary>Advances an ordered marker (`3.` → `4.`, `5)` → `6`)); bullets unchanged.</summary>
    public static string NextMarker(string marker)
    {
        if (marker.Length < 2 || !char.IsDigit(marker[0]))
            return marker; // bullet or degenerate
        if (!int.TryParse(marker[..^1], out var n))
            return marker;
        return (n + 1).ToString() + marker[^1];
    }

    /// <summary>True when the item has no content after the marker/task — Enter should exit the list.</summary>
    public static bool IsEmpty(ListItemInfo item) => string.IsNullOrWhiteSpace(item.Content);

    /// <summary>The prefix written on the continuation line (indent + marker + task reset to unchecked).</summary>
    public static string BuildNextLinePrefix(ListItemInfo item) =>
        item.Indent + NextMarker(item.Marker) + " " + item.Task;

    /// <summary>
    /// Fence-state parity check over the lines strictly before the current
    /// line. Kept intentionally as a line scanner: this runs on every Enter
    /// keypress, where a full Markdig parse (~ms on large docs) would be too
    /// slow; the outline panel does its own parsed check. Parity handles the
    /// standard ```` ```lang ```` … ```` ``` ```` case; exotic reopenings are
    /// approximated, which is acceptable for a typing assist.
    /// </summary>
    public static bool IsInsideFence(IEnumerable<string> linesBeforeCurrent)
    {
        char? fence = null;
        foreach (var line in linesBeforeCurrent)
        {
            var m = FencePattern.Match(line);
            if (!m.Success)
                continue;
            var ch = m.Groups["fence"].Value[0];
            if (fence == null)
                fence = ch;
            else if (fence == ch)
                fence = null;
        }
        return fence != null;
    }

    public static bool IsInsideFence(IReadOnlyList<string> lines, int lineIndex) =>
        IsInsideFence(lines.Take(lineIndex));
}
