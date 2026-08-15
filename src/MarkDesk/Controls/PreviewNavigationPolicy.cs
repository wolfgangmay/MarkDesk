namespace MarkDesk.Controls;

public enum PreviewNavigationKind
{
    /// <summary>In-page fragment jump (#anchor) on the current document — allow.</summary>
    InPageFragment,

    /// <summary>
    /// Reload of the current document (href="" or repeated URL) — cancel
    /// quietly: the rendered page is a temp file that may already have been
    /// replaced by the next render, and reloading it would show an error page.
    /// </summary>
    SelfReload,

    /// <summary>Navigation away from the preview document — cancel and explain.</summary>
    External
}

/// <summary>
/// Classifies a navigation against the preview's current source. The preview
/// document is loaded from a temp file (file:///…/MarkDesk/html-*.html), so
/// "stays on the current document" must be decided by comparing the target
/// against the live source URL — not by a fixed scheme whitelist (the old
/// about:blank / mdlocal whitelist broke when navigation moved to temp files).
/// </summary>
public static class PreviewNavigationPolicy
{
    public static PreviewNavigationKind Classify(string? uri, string? source)
    {
        uri ??= string.Empty;
        source ??= string.Empty;

        var fragmentIndex = uri.IndexOf('#');
        var target =
            fragmentIndex > 0 ? uri[..fragmentIndex]
            : fragmentIndex == 0 ? string.Empty
            : uri;

        var sourceIndex = source.IndexOf('#');
        var sourceBase = sourceIndex >= 0 ? source[..sourceIndex] : source;

        // A fragment-only URI ("#anchor") always stays on the current page.
        if (fragmentIndex == 0)
            return PreviewNavigationKind.InPageFragment;

        if (target == "about:blank" ||
            string.Equals(target, sourceBase, StringComparison.OrdinalIgnoreCase))
        {
            return fragmentIndex > 0 ? PreviewNavigationKind.InPageFragment : PreviewNavigationKind.SelfReload;
        }

        return PreviewNavigationKind.External;
    }
}