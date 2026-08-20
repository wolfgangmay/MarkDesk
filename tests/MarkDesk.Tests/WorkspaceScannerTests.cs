using System.IO;
using MarkDesk.Services;

namespace MarkDesk.Tests;

public class WorkspaceScannerTests : IDisposable
{
    private readonly string _root;

    public WorkspaceScannerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "MarkDeskWs_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public void ListEntries_ReturnsDirectoriesFirstThenFiles_Alphabetical()
    {
        Directory.CreateDirectory(Path.Combine(_root, "notes"));
        Directory.CreateDirectory(Path.Combine(_root, "docs"));
        File.WriteAllText(Path.Combine(_root, "b.md"), "b");
        File.WriteAllText(Path.Combine(_root, "a.md"), "a");
        File.WriteAllText(Path.Combine(_root, "ignored.txt"), "no");

        var entries = WorkspaceScanner.ListEntries(_root);

        Assert.Equal(
            new[] { "docs", "notes", "a.md", "b.md" },
            entries.Select(e => e.Name).ToArray());
        Assert.All(entries.Take(2), e => Assert.True(e.IsDirectory));
        Assert.All(entries.Skip(2), e => Assert.False(e.IsDirectory));
    }

    [Fact]
    public void ListEntries_SkipsBuildFolders_AndOnlyMarkdownFiles()
    {
        foreach (var skip in new[] { "bin", "obj", "node_modules", ".git" })
            Directory.CreateDirectory(Path.Combine(_root, skip));
        File.WriteAllText(Path.Combine(_root, "readme.markdown"), "x");
        File.WriteAllText(Path.Combine(_root, "image.png"), "x");

        var entries = WorkspaceScanner.ListEntries(_root);

        var only = Assert.Single(entries);
        Assert.Equal("readme.markdown", only.Name);
        Assert.False(only.IsDirectory);
    }

    [Fact]
    public void ListEntries_MissingDirectory_ReturnsEmpty()
    {
        var entries = WorkspaceScanner.ListEntries(Path.Combine(_root, "does-not-exist"));
        Assert.Empty(entries);
    }

    [Fact]
    public void ParentOf_DriveRoot_IsNull()
    {
        Assert.Null(WorkspaceScanner.ParentOf(Path.GetPathRoot(Path.GetFullPath(_root))!));
    }

    [Fact]
    public void ParentOf_Subdirectory_IsParent()
    {
        var parent = WorkspaceScanner.ParentOf(_root);
        Assert.Equal(Path.GetDirectoryName(Path.GetFullPath(_root)), parent);
    }
}
