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
}
