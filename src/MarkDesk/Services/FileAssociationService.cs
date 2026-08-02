using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace MarkDesk.Services;

/// <summary>
/// Registers MarkDesk as a handler for Markdown file extensions (.md, .markdown)
/// under the current user's registry hive (HKCU) — no administrator privileges
/// required. On Windows 10/11 the per-user UserChoice hash may still need to be
/// confirmed once via Settings → Apps → Default apps.
/// </summary>
public static class FileAssociationService
{
    private const string ProgId = "MarkDesk.md";

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

    private const uint SHCNE_ASSOCCHANGED = 0x08000000;
    private const uint SHCNF_IDLIST = 0x0000;

    /// <summary>Registers .md/.markdown to open with the current MarkDesk executable.</summary>
    /// <returns>true on success; false if the exe path could not be resolved.</returns>
    public static bool Register(params string[] extensions)
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
            return false;

        try
        {
            // 1. ProgID entry: how to open + icon + friendly name.
            using (var progKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ProgId}"))
            {
                progKey.SetValue(null, "MarkDesk Document");
                using (var iconKey = progKey.CreateSubKey("DefaultIcon"))
                    iconKey.SetValue(null, $"\"{exe}\",0");
                using (var cmdKey = progKey.CreateSubKey(@"shell\open\command"))
                    cmdKey.SetValue(null, $"\"{exe}\" \"%1\"");
            }

            // 2. Map each extension to this ProgID.
            foreach (var raw in extensions)
            {
                var ext = raw.StartsWith('.') ? raw : "." + raw;
                using var extKey = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{ext}");
                extKey.SetValue(null, ProgId);
            }

            // 3. Tell Explorer to refresh its file-type cache.
            SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
