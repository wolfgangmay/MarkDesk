using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace MarkDesk.Services;

/// <summary>
/// Applies the DWM immersive dark/light title bar to a window, based on the
/// current application-level theme (brightness of WindowBgBrush).
/// </summary>
public static class WindowTheme
{
    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int cbAttribute);

    /// <summary>Returns true if the current app theme is dark.</summary>
    public static bool IsDarkTheme()
    {
        if (Application.Current?.Resources["WindowBgBrush"] is SolidColorBrush b)
            return ((b.Color.R + b.Color.G + b.Color.B) / 3) < 96;
        return false;
    }

    public static void ApplyTitleBar(Window window)
    {
        if (window == null)
            return;
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero)
            {
                // Handle not ready yet (e.g. before source initialized); retry once on source initialized.
                window.SourceInitialized -= RetryOnSource;
                window.SourceInitialized += RetryOnSource;
                return;
            }
            var dark = IsDarkTheme() ? 1 : 0;
            // DWMWA_USE_IMMERSIVE_DARK_MODE: attr 20 (Win10 2004+), fallback 19 (1809+)
            if (DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int)) != 0)
                DwmSetWindowAttribute(hwnd, 19, ref dark, sizeof(int));
        }
        catch
        {
            // Pre-Win10 1809: immersive dark mode unsupported; ignore.
        }
    }

    private static void RetryOnSource(object? sender, EventArgs e)
    {
        if (sender is Window w)
        {
            w.SourceInitialized -= RetryOnSource;
            ApplyTitleBar(w);
        }
    }
}
