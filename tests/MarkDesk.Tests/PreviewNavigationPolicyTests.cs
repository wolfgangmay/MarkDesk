using MarkDesk.Controls;

namespace MarkDesk.Tests;

public class PreviewNavigationPolicyTests
{
    private const string TempPage =
        "file:///C:/Users/david/AppData/Local/Temp/MarkDesk/html-d9f85bc98046435594d3ba9170a4bbd6.html";

    [Fact]
    public void AnchorOnTempFilePage_IsInPageFragment()
    {
        // Clicking [link](#heading) while the preview lives at a temp file.
        Assert.Equal(PreviewNavigationKind.InPageFragment,
            PreviewNavigationPolicy.Classify(TempPage + "#installation", TempPage));
    }

    [Fact]
    public void EmptyHashLink_IsInPageFragment()
    {
        // [text](#) resolves to the page URL with a bare fragment.
        Assert.Equal(PreviewNavigationKind.InPageFragment,
            PreviewNavigationPolicy.Classify(TempPage + "#", TempPage));
    }

    [Fact]
    public void BareFragmentUri_IsInPageFragment()
    {
        Assert.Equal(PreviewNavigationKind.InPageFragment,
            PreviewNavigationPolicy.Classify("#section", TempPage));
    }

    [Fact]
    public void ExactSelfNavigation_IsSelfReload()
    {
        // href="" resolves to the current page URL — a reload, not a jump.
        Assert.Equal(PreviewNavigationKind.SelfReload,
            PreviewNavigationPolicy.Classify(TempPage, TempPage));
    }

    [Fact]
    public void AboutBlankFragment_IsInPageFragment()
    {
        // First render still sits on about:blank while the temp file loads.
        Assert.Equal(PreviewNavigationKind.InPageFragment,
            PreviewNavigationPolicy.Classify("about:blank#top", "about:blank"));
    }

    [Fact]
    public void ExternalHttp_IsExternal()
    {
        Assert.Equal(PreviewNavigationKind.External,
            PreviewNavigationPolicy.Classify("https://example.com/doc", TempPage));
    }

    [Fact]
    public void ExternalFragment_IsExternal()
    {
        Assert.Equal(PreviewNavigationKind.External,
            PreviewNavigationPolicy.Classify("https://example.com/doc#intro", TempPage));
    }

    [Fact]
    public void DifferentTempFile_IsExternal()
    {
        // The next render's temp file is a different document.
        Assert.Equal(PreviewNavigationKind.External,
            PreviewNavigationPolicy.Classify(
                "file:///C:/Users/david/AppData/Local/Temp/MarkDesk/html-ffffffffffffffffffffffffffffffff.html",
                TempPage));
    }

    [Fact]
    public void CaseInsensitivePathMatch_IsInPageFragment()
    {
        Assert.Equal(PreviewNavigationKind.InPageFragment,
            PreviewNavigationPolicy.Classify(
                TempPage.ToUpperInvariant() + "#anchor", TempPage));
    }

    [Fact]
    public void NullUriAndSource_CancelQuietly()
    {
        // Degenerate input (no page yet, no target): never classify as
        // External — that would show a dialog with an empty URL.
        Assert.Equal(PreviewNavigationKind.SelfReload,
            PreviewNavigationPolicy.Classify(null, null));
    }
}