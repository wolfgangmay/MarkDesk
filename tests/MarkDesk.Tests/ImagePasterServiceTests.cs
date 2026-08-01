using System.IO;
using MarkDesk.Services;

namespace MarkDesk.Tests;

public class ImagePasterServiceTests
{
    private static string NewDir() =>
        Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "MarkDeskImg_" + Guid.NewGuid().ToString("N"))).FullName;

    [Fact]
    public void ResolveUniqueName_UsesN_Counter_WhenNoConflict()
    {
        var folder = NewDir();
        try
        {
            var name = ImagePasterService.ResolveUniqueName("img-{yyyyMMdd-HHmmss}-{n}", ".png", folder);
            Assert.Matches(@"^img-\d{8}-\d{6}-1\.png$", name);
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [Fact]
    public void ResolveUniqueName_IncrementsN_OnCollision()
    {
        var folder = NewDir();
        try
        {
            File.WriteAllText(Path.Combine(folder, "img-20260101-120000-1.png"), "");
            File.WriteAllText(Path.Combine(folder, "img-20260101-120000-2.png"), "");

            var fixedNow = new DateTime(2026, 1, 1, 12, 0, 0);
            var baseName = "img-{yyyyMMdd-HHmmss}-{n}"
                .Replace("{yyyyMMdd-HHmmss}", fixedNow.ToString("yyyyMMdd-HHmmss"));

            var name = ImagePasterService.ResolveUniqueName(baseName, ".png", folder);
            Assert.Equal("img-20260101-120000-3.png", name);
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [Fact]
    public void ResolveUniqueName_FallsBackToDashN_WhenPatternHasNoN()
    {
        var folder = NewDir();
        try
        {
            File.WriteAllText(Path.Combine(folder, "shot.png"), "");
            var name = ImagePasterService.ResolveUniqueName("shot", ".png", folder);
            Assert.Equal("shot-1.png", name);
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [Fact]
    public void SaveImage_WritesFile_AndReturnsRelativeLink()
    {
        var folder = NewDir();
        var docPath = Path.Combine(folder, "doc.md");
        File.WriteAllText(docPath, "");
        var paster = new ImagePasterService(() => "assets", () => "img-{yyyyMMdd-HHmmss}-{n}");

        try
        {
            var result = paster.SaveImage(new byte[] { 1, 2, 3 }, ".png", docPath);

            Assert.StartsWith("![](assets/", result.MarkdownLink);
            Assert.EndsWith(".png)", result.MarkdownLink);
            var savedFile = Path.Combine(folder, result.SavedRelativePath.Replace('/', Path.DirectorySeparatorChar));
            Assert.True(File.Exists(savedFile));
            Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(savedFile));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [Fact]
    public void SaveImage_Throws_WhenDocumentNotSaved()
    {
        var paster = new ImagePasterService(() => "assets", () => "img-{n}");

        Assert.Throws<InvalidOperationException>(() => paster.SaveImage(new byte[] { 1 }, ".png", null));
    }

    [Fact]
    public void SaveImage_NormalizesExtension()
    {
        var folder = NewDir();
        var docPath = Path.Combine(folder, "doc.md");
        File.WriteAllText(docPath, "");
        var paster = new ImagePasterService(() => "assets", () => "img-{n}");

        try
        {
            var result = paster.SaveImage(new byte[] { 1 }, "PNG", docPath);
            Assert.EndsWith(".png)", result.MarkdownLink);
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }
}
