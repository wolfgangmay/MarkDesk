using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MarkDesk.Models;
using Microsoft.Web.WebView2.Core;

namespace MarkDesk.Controls;

public partial class PreviewView : UserControl, IDisposable
{
    private const string VirtualHost = "mdlocal";
    private const string AssetsHost = "mdassets";

    private string? _pendingHtml;
    private string? _pendingFolder;
    private string? _mappedFolder;
    private bool _assetsMapped;
    private bool _initialized;
    private Task _initTask = Task.CompletedTask;
    private readonly SemaphoreSlim _navigationLock = new(1, 1);
    private double _previewZoom = 1.0;

    // Serialize PDF exports (a temporary offscreen WebView2 is created per export).
    private readonly SemaphoreSlim _printLock = new(1, 1);

    public double PreviewZoom => _previewZoom;
    public event EventHandler? ZoomChanged;

    private static string AssetsFolder =>
        Path.Combine(AppContext.BaseDirectory, "Assets", "web");

    public PreviewView()
    {
        InitializeComponent();
    }

    public bool IsReady => _initialized;

    public async Task UpdateAsync(string html, string? documentFolder)
    {
        LoadingHint.Visibility = _initialized ? Visibility.Collapsed : Visibility.Visible;

        if (_initialized)
        {
            await ApplyAsync(html, documentFolder);
            return;
        }

        _pendingHtml = html;
        _pendingFolder = documentFolder;
        await EnsureInitializedAsync();
    }

    public async Task SetScrollProportionAsync(double proportion)
    {
        if (!_initialized)
            return;
        var clamped = Math.Round(Math.Clamp(proportion, 0, 1), 4)
            .ToString(System.Globalization.CultureInfo.InvariantCulture);
        await WebView.CoreWebView2.ExecuteScriptAsync(
            $"window.scrollTo(0,{clamped}*(document.documentElement.scrollHeight-window.innerHeight))");
    }

    public async Task<bool> PrintToPdfAsync(string html, string? documentFolder, string outputPath, PdfPageSize pageSize)
    {
        await _printLock.WaitAsync();
        Microsoft.Web.WebView2.Wpf.WebView2? printWv = null;
        try
        {
            // Create a fresh offscreen WebView2 for this export (own environment +
            // user-data folder), so the on-screen preview is never disturbed and
            // no Chromium process tree stays resident between exports (#1).
            printWv = new Microsoft.Web.WebView2.Wpf.WebView2();
            PrintHost.Children.Add(printWv);

            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MarkDesk", "WebView2-Print");
            Directory.CreateDirectory(userDataFolder);

            var environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            await printWv.EnsureCoreWebView2Async(environment);
            var core = printWv.CoreWebView2;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;

            // Virtual-host mappings for this export only.
            var folder = documentFolder ?? Path.GetTempPath();
            core.SetVirtualHostNameToFolderMapping(VirtualHost, folder, CoreWebView2HostResourceAccessKind.Allow);
            if (Directory.Exists(AssetsFolder))
                core.SetVirtualHostNameToFolderMapping(AssetsHost, AssetsFolder, CoreWebView2HostResourceAccessKind.Allow);

            var navDone = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            ulong targetNavigationId = 0;
            var waitingForStart = true;

            void Starting(object? s, CoreWebView2NavigationStartingEventArgs e)
            {
                if (!waitingForStart) return;
                waitingForStart = false;
                targetNavigationId = e.NavigationId;
            }

            void Completed(object? s, CoreWebView2NavigationCompletedEventArgs e)
            {
                if (waitingForStart || e.NavigationId != targetNavigationId) return;
                core.NavigationCompleted -= Completed;
                core.NavigationStarting -= Starting;
                navDone.TrySetResult(e.IsSuccess);
            }

            core.NavigationStarting += Starting;
            core.NavigationCompleted += Completed;
            core.NavigateToString(html);

            var navigation = await Task.WhenAny(navDone.Task, Task.Delay(TimeSpan.FromSeconds(10)));
            if (navigation != navDone.Task)
            {
                core.NavigationCompleted -= Completed;
                core.NavigationStarting -= Starting;
                return false;
            }

            if (!await navDone.Task)
                return false;

            var settings = environment.CreatePrintSettings();
            settings.ShouldPrintBackgrounds = true;
            settings.ShouldPrintHeaderAndFooter = false;
            ApplyPageSize(settings, pageSize);

            await core.PrintToPdfAsync(outputPath, settings);
            return true;
        }
        finally
        {
            // Release the temporary WebView2 and its Chromium processes.
            try { printWv?.Dispose(); } catch { }
            if (printWv != null) PrintHost.Children.Remove(printWv);
            _printLock.Release();
        }
    }

    private Task EnsureInitializedAsync()
    {
        if (_initialized)
            return Task.CompletedTask;

        if (!_initTask.IsCompleted)
            return _initTask;

        _initTask = InitializeCoreAsync();
        return _initTask;
    }

    private async Task InitializeCoreAsync()
    {
        try
        {
            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MarkDesk", "WebView2");
            Directory.CreateDirectory(userDataFolder);

            var environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            await WebView.EnsureCoreWebView2Async(environment);

            WebView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            WebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;

            _initialized = true;
            LoadingHint.Visibility = Visibility.Collapsed;

            if (_pendingHtml != null)
            {
                var html = _pendingHtml;
                var folder = _pendingFolder;
                _pendingHtml = null;
                _pendingFolder = null;
                await ApplyAsync(html, folder);
            }
        }
        catch (Exception ex)
        {
            LoadingHint.Text = "WebView2 unavailable: " + ex.Message;
            throw;
        }
    }

    private async Task ApplyAsync(string html, string? documentFolder)
    {
        EnsureFolderMapping(documentFolder);
        await _navigationLock.WaitAsync();
        try
        {
            WebView.CoreWebView2.NavigateToString(html);
            await WaitNavigationAsync();
            await ApplyZoomAsync();
        }
        finally
        {
            _navigationLock.Release();
        }
    }

    private async Task WaitNavigationAsync()
    {
        var navDone = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        ulong targetNavigationId = 0;
        var waitingForStart = true;

        void Starting(object? s, CoreWebView2NavigationStartingEventArgs e)
        {
            if (!waitingForStart)
                return;
            waitingForStart = false;
            targetNavigationId = e.NavigationId;
        }

        void Completed(object? s, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (waitingForStart || e.NavigationId != targetNavigationId)
                return;
            WebView.CoreWebView2.NavigationCompleted -= Completed;
            WebView.CoreWebView2.NavigationStarting -= Starting;
            navDone.TrySetResult(e.IsSuccess);
        }

        WebView.CoreWebView2.NavigationStarting += Starting;
        WebView.CoreWebView2.NavigationCompleted += Completed;

        var winner = await Task.WhenAny(navDone.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        if (winner != navDone.Task)
        {
            WebView.CoreWebView2.NavigationCompleted -= Completed;
            WebView.CoreWebView2.NavigationStarting -= Starting;
            return;
        }
        await navDone.Task;
    }

    private void WebView_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0 || !_initialized)
            return;
        e.Handled = true;
        _previewZoom = Math.Clamp(Math.Round(_previewZoom + (e.Delta > 0 ? 0.1 : -0.1), 2), 0.3, 3.0);
        _ = ApplyZoomAsync();
        ZoomChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ResetZoom()
    {
        _previewZoom = 1.0;
        _ = ApplyZoomAsync();
        ZoomChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task ApplyZoomAsync()
    {
        if (!_initialized)
            return;
        var z = _previewZoom.ToString(System.Globalization.CultureInfo.InvariantCulture);
        try
        {
            await WebView.CoreWebView2.ExecuteScriptAsync($"document.body.style.zoom={z}");
        }
        catch
        {
            // WebView2 temporarily unavailable; zoom reapplied on next render.
        }
    }

    private void EnsureFolderMapping(string? folder)
    {
        folder ??= Path.GetTempPath();

        if (string.Equals(_mappedFolder, folder, StringComparison.OrdinalIgnoreCase))
            return;

        try { WebView.CoreWebView2.ClearVirtualHostNameToFolderMapping(VirtualHost); }
        catch { /* not mapped yet */ }

        WebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
            VirtualHost, folder, CoreWebView2HostResourceAccessKind.Allow);
        _mappedFolder = folder;

        EnsureAssetsMapping();
    }

    private void EnsureAssetsMapping()
    {
        if (_assetsMapped)
            return;
        try
        {
            if (Directory.Exists(AssetsFolder))
            {
                WebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    AssetsHost, AssetsFolder, CoreWebView2HostResourceAccessKind.Allow);
                _assetsMapped = true;
            }
        }
        catch
        {
            // Assets optional; degrade gracefully (no highlighting / math).
        }
    }

    private static void ApplyPageSize(CoreWebView2PrintSettings settings, PdfPageSize pageSize)
    {
        settings.PageWidth = pageSize == PdfPageSize.Letter ? 8.5 : 8.27;
        settings.PageHeight = pageSize == PdfPageSize.Letter ? 11.0 : 11.69;
    }

    // #2 hygiene: dispose the on-screen WebView2 on shutdown so its Chromium
    // children don't leak as orphans if the app is forcibly terminated. (The
    // offscreen print WebView is already disposed after each export — see #1.)
    public void Dispose()
    {
        try { WebView.Dispose(); } catch { }
        foreach (System.Windows.UIElement child in PrintHost.Children)
            try { (child as Microsoft.Web.WebView2.Wpf.WebView2)?.Dispose(); } catch { }
    }
}
