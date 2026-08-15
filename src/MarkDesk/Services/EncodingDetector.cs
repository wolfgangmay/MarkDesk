using System.Text;

namespace MarkDesk.Services;

public sealed class EncodingDetector : IEncodingDetector
{
    private static readonly UTF8Encoding Utf8Strict =
        new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>
    /// Detection only ever inspects the first few KB. Whole-file scans made
    /// opening large documents measurably slower; a 4 KB probe is plenty to
    /// tell BOM/UTF-8/GBK/UTF-16 apart (the first high byte decides).
    /// </summary>
    private const int ProbeBytes = 4096;

    public DetectedEncoding Detect(byte[] bytes)
    {
        // 1. BOM detection
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return new(new UTF8Encoding(true), "UTF-8-BOM", true);

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return new(Encoding.Unicode, "UTF-16LE", true);

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return new(Encoding.BigEndianUnicode, "UTF-16BE", true);

        var probe = bytes.Length <= ProbeBytes
            ? bytes
            : bytes.AsSpan(0, ProbeBytes).ToArray();

        // 2. Strict UTF-8 validation (no BOM) — probe only. The probe can cut
        // a multi-byte character in half at the boundary, so on failure retry
        // with up to 3 trailing bytes dropped (UTF-8 chars span at most 4
        // bytes); without this, files whose 4096th byte lands inside a CJK
        // character were misdetected as GBK and rendered as mojibake.
        if (IsValidUtf8(probe) || IsValidUtf8Truncated(probe))
            return new(new UTF8Encoding(false), "UTF-8", false);

        // 3. UTF-16LE without BOM (NUL-heavy byte pattern, would otherwise mangle as GBK)
        if (LooksLikeUtf16Le(probe))
            return new(Encoding.Unicode, "UTF-16LE", false);

        // 4. GBK fallback (best effort for other non-UTF-8 text)
        return new(GetGbkEncoding(), "GBK", false);
    }

    private static bool LooksLikeUtf16Le(byte[] bytes)
    {
        if (bytes.Length < 4 || bytes.Length % 2 != 0)
            return false;

        var nuls = 0;
        for (var i = 1; i < bytes.Length; i += 2)
            if (bytes[i] == 0)
                nuls++;
        return nuls >= bytes.Length / 2 * 7 / 10;
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

    /// <summary>
    /// True when the probe is valid UTF-8 once up to 3 trailing bytes are
    /// ignored — i.e. the only defect is a multi-byte character truncated by
    /// the probe boundary.
    /// </summary>
    private static bool IsValidUtf8Truncated(byte[] bytes)
    {
        for (var drop = 1; drop <= 3 && bytes.Length > drop; drop++)
        {
            try
            {
                Utf8Strict.GetString(bytes, 0, bytes.Length - drop);
                return true;
            }
            catch (DecoderFallbackException)
            {
                // invalid earlier in the probe — try dropping one more byte
            }
        }
        return false;
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
