using System.IO;
using System.Text;

namespace MarkDesk.Services;

public interface IImagePasterService
{
    ImagePasteResult SaveImage(byte[] imageBytes, string extension, string? documentPath);
}

public sealed record ImagePasteResult(string MarkdownLink, string SavedRelativePath);

public sealed class ImagePasterService : IImagePasterService
{
    private readonly Func<string> _assetsFolderProvider;
    private readonly Func<string> _namePatternProvider;

    public ImagePasterService(ISettingsService settingsService)
        : this(() => settingsService.Current.AssetsFolderName, () => settingsService.Current.ImageNamePattern)
    {
    }

    public ImagePasterService(Func<string> assetsFolderProvider, Func<string> namePatternProvider)
    {
        _assetsFolderProvider = assetsFolderProvider;
        _namePatternProvider = namePatternProvider;
    }

    public ImagePasteResult SaveImage(byte[] imageBytes, string extension, string? documentPath)
    {
        if (documentPath == null)
            throw new InvalidOperationException("Document must be saved before pasting images.");

        var documentFolder = Path.GetDirectoryName(documentPath)
            ?? throw new InvalidOperationException("Cannot resolve document folder.");

        var assetsFolder = Path.Combine(documentFolder, _assetsFolderProvider());
        Directory.CreateDirectory(assetsFolder);

        extension = NormalizeExtension(extension);
        var fileName = ResolveUniqueName(_namePatternProvider(), extension, assetsFolder);
        var fullPath = Path.Combine(assetsFolder, fileName);

        var resolvedFull = Path.GetFullPath(fullPath);
        var resolvedAssets = Path.GetFullPath(assetsFolder);
        var prefix = resolvedAssets.EndsWith(Path.DirectorySeparatorChar)
            ? resolvedAssets
            : resolvedAssets + Path.DirectorySeparatorChar;
        if (!resolvedFull.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Resolved image path escapes the assets folder.");

        File.WriteAllBytes(fullPath, imageBytes);

        var relativePath = $"{_assetsFolderProvider()}/{fileName}";
        var link = $"![]({relativePath})";
        return new ImagePasteResult(link, relativePath);
    }

    private static string NormalizeExtension(string extension)
    {
        extension = extension?.Trim().ToLowerInvariant() ?? ".png";
        if (!extension.StartsWith('.'))
            extension = "." + extension;
        return extension;
    }

    internal static string ResolveUniqueName(string pattern, string extension, string folder)
    {
        var baseName = ResolveDateTokens(pattern, DateTime.Now);

        if (baseName.Contains("{n}", StringComparison.Ordinal))
        {
            for (var n = 1; ; n++)
            {
                var candidate = baseName.Replace("{n}", n.ToString(), StringComparison.Ordinal) + extension;
                if (!File.Exists(Path.Combine(folder, candidate)))
                    return candidate;
            }
        }

        var direct = baseName + extension;
        if (!File.Exists(Path.Combine(folder, direct)))
            return direct;

        for (var n = 1; ; n++)
        {
            var candidate = $"{baseName}-{n}{extension}";
            if (!File.Exists(Path.Combine(folder, candidate)))
                return candidate;
        }
    }

    private static string ResolveDateTokens(string pattern, DateTime now)
    {
        var result = new StringBuilder();
        var i = 0;
        while (i < pattern.Length)
        {
            if (pattern[i] == '{')
            {
                var end = pattern.IndexOf('}', i);
                if (end > i)
                {
                    var token = pattern.Substring(i + 1, end - i - 1);
                    if (token == "n")
                        result.Append("{n}");
                    else
                        result.Append(FormatNow(now, token));
                    i = end + 1;
                    continue;
                }
            }
            result.Append(pattern[i]);
            i++;
        }
        return result.ToString();
    }

    private static string FormatNow(DateTime now, string format)
    {
        try { return now.ToString(format); }
        catch { return format; }
    }
}
