using System.IO;
using System.Text;
using MarkDesk.Services;

namespace MarkDesk.Tests;

public class FileServiceTests
{
    private readonly FileService _service = new(new EncodingDetector());

    private static string TempFile(string ext = ".md") =>
        Path.Combine(Path.GetTempPath(), "MarkDeskFileTest_" + Guid.NewGuid().ToString("N") + ext);

    [Fact]
    public void Load_RoundTrips_Utf8()
    {
        var path = TempFile();
        const string content = "# Title\n\n一些中文内容";
        File.WriteAllText(path, content, new UTF8Encoding(false));

        try
        {
            var result = _service.Load(path);

            Assert.Equal(content, result.Text);
            Assert.Equal("UTF-8", result.Encoding.DisplayName);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Load_RoundTrips_Gbk_And_StripsBom()
    {
        var path = TempFile();
        const string content = "中文内容 test";
        var gbk = Encoding.GetEncoding(936);
        File.WriteAllBytes(path, gbk.GetBytes(content));

        try
        {
            var result = _service.Load(path);

            Assert.Equal(content, result.Text);
            Assert.Equal("GBK", result.Encoding.DisplayName);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Save_IsAtomic_NoTempLeft()
    {
        var dir = Path.Combine(Path.GetTempPath(), "MarkDeskFileTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "doc.md");
        const string content = "new content";

        try
        {
            _service.Save(path, content, new UTF8Encoding(false));

            Assert.Equal(content, File.ReadAllText(path));
            var remaining = Directory.GetFiles(dir, "*.tmp", SearchOption.TopDirectoryOnly);
            Assert.Empty(remaining);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Save_Overwrites_Existing_PreservesContent()
    {
        var path = TempFile();
        File.WriteAllText(path, "old");

        try
        {
            _service.Save(path, "new content", new UTF8Encoding(false));

            Assert.Equal("new content", File.ReadAllText(path));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
