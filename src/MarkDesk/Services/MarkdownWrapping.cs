namespace MarkDesk.Services;

/// <summary>
/// Pure functions for the Markdown typing assist: selection wrapping and
/// token toggling (bold / italic / inline code / link).
/// </summary>
public static class MarkdownWrapping
{
    /// <summary>Wraps <paramref name="text"/> with the opening/closing token.</summary>
    public static string Wrap(string text, string token) => token + text + token;

    /// <summary>
    /// Unwraps <paramref name="text"/> when it starts and ends with
    /// <paramref name="token"/>; otherwise returns the input unchanged.
    /// </summary>
    public static string Unwrap(string text, string token) =>
        text.Length >= token.Length * 2 &&
        text.StartsWith(token, StringComparison.Ordinal) &&
        text.EndsWith(token, StringComparison.Ordinal)
            ? text[token.Length..^token.Length]
            : text;

    /// <summary>True when <paramref name="text"/> is fully wrapped by the token.</summary>
    public static bool IsWrapped(string text, string token) =>
        text.Length >= token.Length * 2 &&
        text.StartsWith(token, StringComparison.Ordinal) &&
        text.EndsWith(token, StringComparison.Ordinal);

    /// <summary>Wraps or unwraps (toggle semantics, VS Code style).</summary>
    public static string Toggle(string text, string token) =>
        IsWrapped(text, token) ? Unwrap(text, token) : Wrap(text, token);

    /// <summary>
    /// Computes the replacement for a link toggle: <c>[text](url)</c> when
    /// plain, unwrapped back to text when already a link with empty or
    /// placeholder target. Returns null when not applicable.
    /// </summary>
    public static string? ToggleLink(string text, string url = "")
    {
        if (text.Length >= 4 && text.StartsWith("[", StringComparison.Ordinal))
        {
            var close = text.IndexOf("](", StringComparison.Ordinal);
            if (close > 0 && text.EndsWith(")", StringComparison.Ordinal) &&
                close + 2 < text.Length)
                return text[1..close]; // strip link, keep label
        }
        return $"[{text}]({url})";
    }

    /// <summary>Characters that auto-close their pair when typed bare: ` [ (</summary>
    public static bool IsPairedChar(char c) => c is '`' or '[' or '(';

    public static char ClosingFor(char c) => c switch
    {
        '`' => '`',
        '[' => ']',
        '(' => ')',
        _ => c,
    };
}
