using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MarkDesk.Models;
using Microsoft.Web.WebView2.Core;

namespace MarkDesk.Controls;

public partial class PreviewView : UserControl
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

    // Separate, offscreen WebView2 for PDF export (own environment + user data
    // folder). Keeps export navigation from flashing the on-screen preview.
    private bool _printInitialized;
    private Task _printInitTask = Task.CompletedTask;
    private readonly SemaphoreSlim _printLock = new(1, 1);
    private string? _printMappedFolder;
    private bool _printAssetsMapped;

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
        await EnsurePrintInitializedAsync();
        EnsurePrintFolderMapping(documentFolder);

        await _printLock.WaitAsync();
        try
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
                PrintWebView.CoreWebView2.NavigationCompleted -= Completed;
                PrintWebView.CoreWebView2.NavigationStarting -= Starting;
                navDone.TrySetResult(e.IsSuccess);
            }

            PrintWebView.CoreWebView2.NavigationStarting += Starting;
            PrintWebView.CoreWebView2.NavigationCompleted += Completed;
            PrintWebView.CoreWebView2.NavigateToString(html);

            var navigation = await Task.WhenAny(navDone.Task, Task.Delay(TimeSpan.FromSeconds(10)));
            if (navigation != navDone.Task)
            {
                PrintWebView.CoreWebView2.NavigationCompleted -= Completed;
                PrintWebView.CoreWebView2.NavigationStarting -= Starting;
                return false;
            }

            if (!await navDone.Task)
                return false;

            var settings = PrintWebView.CoreWebView2.Environment.CreatePrintSettings();
            settings.ShouldPrintBackgrounds = true;
            settings.ShouldPrintHeaderAndFooter = false;
            ApplyPageSize(settings, pageSize);

            await PrintWebView.CoreWebView2.PrintToPdfAsync(outputPath, settings);
            return true;
        }
        finally
        {
            _printLock.Release();
        }
    }

    private Task EnsurePrintInitializedAsync()
    {
        if (_printInitialized)
            return Task.CompletedTask;
        if (!_printInitTask.IsCompleted)
            return _printInitTask;
        _printInitTask = InitializePrintCoreAsync();
        return _printInitTask;
    }

    private async Task InitializePrintCoreAsync()
    {
        try
        {
            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MarkDesk", "WebView2-Print");
            Directory.CreateDirectory(userDataFolder);

            var environment = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
            await PrintWebView.EnsureCoreWebView2Async(environment);

            PrintWebView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            PrintWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;

            _printInitialized = true;
        }
        catch
        {
            // Fall back below to the shared on-screen WebView if the offscreen
            // environment cannot be created (e.g. shared-folder lock).
            _printInitialized = false;
        }
    }

    private void EnsurePrintFolderMapping(string? folder)
    {
        folder ??= Path.GetTempPath();

        if (string.Equals(_printMappedFolder, folder, StringComparison.OrdinalIgnoreCase))
        {
            EnsurePrintAssetsMapping();
            return;
        }

        try { PrintWebView.CoreWebView2.ClearVirtualHostNameToFolderMapping(VirtualHost); }
        catch { /* not mapped yet */ }

        PrintWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
            VirtualHost, folder, CoreWebView2HostResourceAccessKind.Allow);
        _printMappedFolder = folder;

        EnsurePrintAssetsMapping();
    }

    private void EnsurePrintAssetsMapping()
    {
        if (_printAssetsMapped)
            return;
        try
        {
            if (Directory.Exists(AssetsFolder))
            {
                PrintWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    AssetsHost, AssetsFolder, CoreWebView2HostResourceAccessKind.Allow);
                _printAssetsMapped = true;
            }
        }
        catch { /* Assets optional; degrade gracefully. */ }
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
}
