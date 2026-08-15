using System.IO;
using System.Text;
using MarkDesk.Services;

namespace MarkDesk.Tests;

public class EncodingDetectorTests
{
    private readonly EncodingDetector _detector = new();

    [Fact]
    public void Detects_Utf8_WithBom()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }
            .Concat(Encoding.UTF8.GetBytes("Hello 中文")).ToArray();

        var result = _detector.Detect(bytes);

        Assert.Equal("UTF-8-BOM", result.DisplayName);
        Assert.True(result.HasBom);
    }

    [Fact]
    public void Detects_Utf8_WhenProbeBoundarySplitsMultibyteChar()
    {
        // Regression: a >4 KB UTF-8 file whose 4096th byte lands inside a CJK
        // character. The strict validation of the truncated probe failed and
        // the file was misdetected as GBK (mojibake) — see pdf-test-cjk.md.
        var prefix = new byte[4095]; // ASCII filler
        Array.Fill(prefix, (byte)'a');
        // "中文" = E4 B8 AD E6 96 87: the lead byte E4 lands exactly at probe
        // index 4095, so the probe ends with a truncated 3-byte character.
        var bytes = prefix.Concat(Encoding.UTF8.GetBytes("中文")).ToArray();

        var result = _detector.Detect(bytes);

        Assert.Equal("UTF-8", result.DisplayName);
        Assert.False(result.HasBom);
    }

    [Fact]
    public void StillDetects_Gbk_WhenProbeIsReallyInvalid()
    {
        // Random invalid-UTF-8 bytes across the whole probe must NOT be
        // rescued by the truncation fallback.
        var bytes = new byte[4096];
        var rnd = new Random(42);
        for (var i = 0; i < bytes.Length; i++)
            bytes[i] = (byte)rnd.Next(0x80, 0xFF);

        var result = _detector.Detect(bytes);

        Assert.Equal("GBK", result.DisplayName);
    }

    [Fact]
    public void Detects_Utf8_WithoutBom()
    {
        var bytes = Encoding.UTF8.GetBytes("# Hello 世界");

        var result = _detector.Detect(bytes);

        Assert.Equal("UTF-8", result.DisplayName);
        Assert.False(result.HasBom);
    }

    [Fact]
    public void Detects_Gbk_WhenNotValidUtf8()
    {
        var gbk = Encoding.GetEncoding(936);
        var bytes = gbk.GetBytes("中文标题"); // GBK bytes are not valid UTF-8

        var result = _detector.Detect(bytes);

        Assert.Equal("GBK", result.DisplayName);
        Assert.False(result.HasBom);
    }

    [Fact]
    public void Detects_Utf16Le_WithoutBom()
    {
        var bytes = Encoding.Unicode.GetBytes("Hello 😀 world");

        var result = _detector.Detect(bytes);

        Assert.Equal("UTF-16LE", result.DisplayName);
        Assert.False(result.HasBom);
    }

    [Fact]
    public void Detects_Empty_AsUtf8()
    {
        var result = _detector.Detect(Array.Empty<byte>());

        Assert.Equal("UTF-8", result.DisplayName);
    }

    [Fact]
    public void Detects_MultiMegabyteFile_FromProbeOnly()
    {
        // 6 MB of valid UTF-8: detection must rely on the 4 KB probe, not a
        // whole-file scan (which was the pre-optimization behaviour).
        var bytes = new byte[6 * 1024 * 1024];
        Encoding.UTF8.GetBytes("## Section\n").CopyTo(bytes, 0);

        var result = _detector.Detect(bytes);

        Assert.Equal("UTF-8", result.DisplayName);
    }

    [Fact]
    public void Detects_Gbk_WhenProbeContainsInvalidBytes()
    {
        // Large file whose first 4 KB contain GBK bytes (not valid UTF-8).
        var gbk = Encoding.GetEncoding(936);
        var head = gbk.GetBytes(new string('汉', 3000)); // ~6 KB of GBK
        var bytes = new byte[6 * 1024 * 1024];
        head.CopyTo(bytes, 0);

        var result = _detector.Detect(bytes);

        Assert.Equal("GBK", result.DisplayName);
    }
}
