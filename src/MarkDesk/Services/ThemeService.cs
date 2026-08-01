using Microsoft.Win32;

namespace MarkDesk.Services;

public static class ThemeService
{
    public static bool IsSystemDark()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch
        {
            return false;
        }
    }

    public static bool IsDark(Models.ThemeMode mode) =>
        mode == Models.ThemeMode.Dark || (mode == Models.ThemeMode.System && IsSystemDark());
}
