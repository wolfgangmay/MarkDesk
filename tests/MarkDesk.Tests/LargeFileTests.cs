using MarkDesk.Services;

namespace MarkDesk.Tests;

public class DocumentTierTests
{
    private const long Mb = 1024 * 1024;

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(1024)]
    [InlineData(DocumentTierResolver.RealTimeThresholdBytes)]
    public void ForBytes_UpTo1Mb_IsRealTime(long bytes)
        => Assert.Equal(DocumentTier.RealTime, DocumentTierResolver.ForBytes(bytes));

    [Theory]
    [InlineData(DocumentTierResolver.RealTimeThresholdBytes + 1)]
    [InlineData(2 * Mb)]
    [InlineData(DocumentTierResolver.LargeThresholdBytes)]
    public void ForBytes_1To5Mb_IsMedium(long bytes)
        => Assert.Equal(DocumentTier.Medium, DocumentTierResolver.ForBytes(bytes));

    [Theory]
    [InlineData(DocumentTierResolver.LargeThresholdBytes + 1)]
    [InlineData(10 * Mb)]
    [InlineData(50L * 1024 * 1024)]
    public void ForBytes_Above5Mb_IsLarge(long bytes)
        => Assert.Equal(DocumentTier.Large, DocumentTierResolver.ForBytes(bytes));

    [Fact]
    public void PdfExportLimit_Is20Mb()
        => Assert.Equal(20L * Mb, DocumentTierResolver.PdfExportLimitBytes);
}

public class RenderGateTests
{
    [Fact]
    public void Claim_OldVersion_IsRejected_AfterNewerClaim()
    {
        var gate = new RenderGate();
        var v1 = gate.Next();
        var v2 = gate.Next();

        Assert.False(gate.TryClaim(v1));
        Assert.True(gate.TryClaim(v2));
    }

    [Fact]
    public void Claim_CurrentVersion_IsAccepted()
    {
        var gate = new RenderGate();
        var v = gate.Next();
        Assert.True(gate.TryClaim(v));
    }

    [Fact]
    public void Versions_AreStrictlyIncreasing()
    {
        var gate = new RenderGate();
        var a = gate.Next();
        var b = gate.Next();
        var c = gate.Next();
        Assert.True(a < b && b < c);
    }
}

public class FastOutlineScannerTests
{
    [Fact]
    public void Extract_FindsAtxHeadings_WithLevelAndOneBasedLine()
    {
        var items = FastOutlineScanner.Extract("# Title\n\n## Section\nBody\n### Sub\n");

        Assert.Equal(3, items.Count);
        Assert.Equal(new OutlineItem(1, 1, "Title"), items[0]);
        Assert.Equal(new OutlineItem(2, 3, "Section"), items[1]);
        Assert.Equal(new OutlineItem(3, 5, "Sub"), items[2]);
    }

    [Fact]
    public void Extract_SkipsHeadingsInsideFence()
    {
        var md = "```\n# Fake\n```\n# Real\n```\n## AlsoFake\n```";
        var items = FastOutlineScanner.Extract(md);

        Assert.Single(items);
        Assert.Equal("Real", items[0].Text);
    }

    [Fact]
    public void Extract_IgnoresFourSpaceIndented()
    {
        var items = FastOutlineScanner.Extract("    # indented\n# real\n");
        Assert.Single(items);
        Assert.Equal("real", items[0].Text);
    }

    [Fact]
    public void Extract_EmptyDocument_ReturnsEmpty()
        => Assert.Empty(FastOutlineScanner.Extract(""));

    [Fact]
    public void Extract_NullDocument_ReturnsEmpty()
        => Assert.Empty(FastOutlineScanner.Extract(null!));

    [Fact]
    public void Extract_KeepsRawFormattingMarkers()
    {
        var items = FastOutlineScanner.Extract("# **bold** text\n");
        Assert.Equal("**bold** text", items[0].Text);
    }

    [Fact]
    public void Extract_EmptyHeading_IsSkipped()
    {
        var items = FastOutlineScanner.Extract("#\n");
        Assert.Empty(items);
    }

    // The scanner must agree with the preview pipeline on ordinary documents.
    [Fact]
    public void Extract_AgreesWithOutlineParser_OnPlainDocument()
    {
        const string md = "# A\n## B\nparagraph\n### C\n- item\n1. item\n> quote\n";
        var fast = FastOutlineScanner.Extract(md);
        var parsed = OutlineParser.Extract(new MarkdownRenderer().Parse(md));

        Assert.Equal(parsed.Select(i => (i.Level, i.Line, i.Text)), fast.Select(i => (i.Level, i.Line, i.Text)));
    }
}