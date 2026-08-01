using System.Text;

namespace MarkDesk.Services;

public interface IEncodingDetector
{
    DetectedEncoding Detect(byte[] bytes);
}

public readonly record struct DetectedEncoding(Encoding Encoding, string DisplayName, bool HasBom);
