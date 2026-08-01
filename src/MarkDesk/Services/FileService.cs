using System.IO;
using System.Text;

namespace MarkDesk.Services;

public interface IFileService
{
    DocumentLoadResult Load(string filePath);
    void Save(string filePath, string content, Encoding encoding);
}

public sealed record DocumentLoadResult(string Text, DetectedEncoding Encoding);

public sealed class FileService : IFileService
{
    private readonly IEncodingDetector _encodingDetector;

    public FileService(IEncodingDetector encodingDetector)
    {
        _encodingDetector = encodingDetector;
    }

    public DocumentLoadResult Load(string filePath)
    {
        var bytes = File.ReadAllBytes(filePath);
        var detected = _encodingDetector.Detect(bytes);
        var text = detected.Encoding.GetString(bytes);

        if (detected.HasBom && text.Length > 0 && text[0] == '\uFEFF')
            text = text[1..];

        return new DocumentLoadResult(text, detected);
    }

    public void Save(string filePath, string content, Encoding encoding)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var tempPath = Path.Combine(
            Path.GetDirectoryName(filePath) ?? string.Empty,
            $".{Path.GetFileNameWithoutExtension(filePath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            File.WriteAllText(tempPath, content, encoding);
            File.Move(tempPath, filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { /* best effort */ }
            }
        }
    }
}
