using System.Text;

namespace MarkDesk.Services;

public sealed class EncodingDetector : IEncodingDetector
{
    private static readonly UTF8Encoding Utf8Strict =
        new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public DetectedEncoding Detect(byte[] bytes)
    {
        // 1. BOM detection
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return new(new UTF8Encoding(true), "UTF-8-BOM", true);

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return new(Encoding.Unicode, "UTF-16LE", true);

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return new(Encoding.BigEndianUnicode, "UTF-16BE", true);

        // 2. Strict UTF-8 validation (no BOM)
        if (IsValidUtf8(bytes))
            return new(new UTF8Encoding(false), "UTF-8", false);

        // 3. GBK fallback
        var gbk = GetGbkEncoding();
        return new(gbk, "GBK", false);
    }

    private static bool IsValidUtf8(byte[] bytes)
    {
        try
        {
            Utf8Strict.GetString(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }

    private static Encoding GetGbkEncoding()
    {
        try
        {
            return Encoding.GetEncoding(936);
        }
        catch
        {
            return Encoding.Default;
        }
    }
}
