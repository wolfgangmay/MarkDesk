using System.IO;

namespace MarkDesk.Services;

/// <summary>A flat entry the files panel shows for one directory level.</summary>
/// <param name="Name">Display name (file name or directory name).</param>
/// <param name="FullPath">Absolute path.</param>
/// <param name="IsDirectory">True for a folder, false for a markdown file.</param>
public readonly record struct WorkspaceEntry(string Name, string FullPath, bool IsDirectory);

/// <summary>
/// Enumerates the markdown files and sub-directories of one directory level
/// for the files panel. Pure logic (no WPF) so it is unit-testable and safe
/// to call from a background thread — directory enumeration on slow/network
/// disks must never block the UI.
/// </summary>
public static class WorkspaceScanner
{
    private static readonly string[] SkippedDirectories =
    {
        "bin", "obj", "node_modules", ".git", ".vs", ".idea", ".codegraph"
    };

    private static readonly HashSet<string> MarkdownExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".md", ".markdown"
    };

    /// <summary>Lists the markdown files and sub-directories of <paramref name="directory"/>.</summary>
    /// <remarks>
    /// Directories sort before files, each group alphabetically (OrdinalIgnoreCase).
    /// Returns an empty list (never throws) when the directory is gone or inaccessible.
    /// </remarks>
    public static List<WorkspaceEntry> ListEntries(string directory)
    {
        var result = new List<WorkspaceEntry>();
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(directory))
                if (!ShouldSkip(Path.GetFileName(dir)))
                    result.Add(new WorkspaceEntry(Path.GetFileName(dir), dir, IsDirectory: true));

            foreach (var file in Directory.EnumerateFiles(directory))
                if (MarkdownExtensions.Contains(Path.GetExtension(file)))
                    result.Add(new WorkspaceEntry(Path.GetFileName(file), file, IsDirectory: false));
        }
        catch (DirectoryNotFoundException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (IOException)
        {
        }

        result.Sort(static (a, b) =>
        {
            if (a.IsDirectory != b.IsDirectory)
                return a.IsDirectory ? -1 : 1;
            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        });
        return result;
    }

    /// <summary>Hidden/system build folders the tree never descends into.</summary>
    public static bool ShouldSkip(string directoryName) =>
        SkippedDirectories.Contains(directoryName, StringComparer.OrdinalIgnoreCase);

    /// <summary>Parent directory, or null at a drive root.</summary>
    public static string? ParentOf(string directory)
    {
        var parent = Directory.GetParent(directory);
        return parent?.FullName;
    }
}
