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

    /// <summary>Raised when the user clicks a rendered block (reverse sync). 1-based line.</summary>
    public event Action<int>? SourceLineRequested;

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

    /// <summary>
    /// Scrolls the rendered document so the block starting at the given
    /// 1-based source line is at the top (outline navigation in Preview mode).
    /// </summary>
    public async Task ScrollToLine(int line)
    {
        if (!_initialized)
            return;
        try
        {
            await WebView.CoreWebView2.ExecuteScriptAsync(
                $"document.querySelector('[data-line=\"{line}\"]')?.scrollIntoView({{ behavior: 'smooth', block: 'start' }});");
        }
        catch
        {
            // WebView2 temporarily unavailable; ignore (next render restores).
        }
    }

    public async Task<bool> PrintToPdfAsync(string html, string? documentFolder, string outputPath, PdfPageSize pageSize, PdfMargins margins)
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
            ApplyMargins(settings, margins);
            await ApplyPrintLayoutAsync(core, pageSize, margins);

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

            WebView.CoreWebView2.NavigationStarting += OnPreviewNavigationStarting;
            WebView.CoreWebView2.NewWindowRequested += OnNewWindowRequested;
            WebView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;

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

    private static void ApplyMargins(CoreWebView2PrintSettings settings, PdfMargins margins)
    {
        settings.MarginTop = PdfMargins.MmToInches(margins.TopMm);
        settings.MarginBottom = PdfMargins.MmToInches(margins.BottomMm);
        settings.MarginLeft = PdfMargins.MmToInches(margins.LeftMm);
        settings.MarginRight = PdfMargins.MmToInches(margins.RightMm);
    }

    // Even-distribution pass ("均匀灌版"): simulate the print layout at the
    // exact content width, then tag short blocks (<= ~30% of the page content
    // height) with .md-keep so Chromium only ever moves cheap blocks to the
    // next page. Long blocks stay splittable, which avoids the large gaps a
    // blanket break-inside:avoid used to leave behind. Any failure degrades
    // silently to the pure-CSS pagination rules.
    private static async Task ApplyPrintLayoutAsync(
        CoreWebView2 core, PdfPageSize pageSize, PdfMargins margins)
    {
        try
        {
            var paperW = pageSize == PdfPageSize.Letter ? 215.9 : 210.0;
            var paperH = pageSize == PdfPageSize.Letter ? 279.4 : 297.0;
            var contentW = ((paperW - margins.LeftMm - margins.RightMm) / 25.4 * 96)
                .ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);
            var contentH = ((paperH - margins.TopMm - margins.BottomMm) / 25.4 * 96)
                .ToString("0.#", System.Globalization.CultureInfo.InvariantCulture);

            var script =
                "(async function(){" +
                "try{ if(window.__mdPrintReady) await window.__mdPrintReady; }catch(e){}" +
                "var b=document.body;" +
                "var prev={w:b.style.width,m:b.style.maxWidth,p:b.style.padding};" +
                "b.style.maxWidth='none';b.style.width='" + contentW + "px';b.style.padding='0';" +
                "await new Promise(function(r){requestAnimationFrame(function(){requestAnimationFrame(r);});});" +
                "var lim=" + contentH + "*0.30;" +
                "document.querySelectorAll('pre,table,blockquote').forEach(function(el){" +
                "if(el.getBoundingClientRect().height<=lim) el.classList.add('md-keep');" +
                "else el.classList.remove('md-keep');" +
                "});" +
                "b.style.width=prev.w;b.style.maxWidth=prev.m;b.style.padding=prev.p;" +
                "return 'ok';" +
                "})()";
            await core.ExecuteScriptAsync(script);
        }
        catch
        {
            // Degrade to pure-CSS pagination.
        }
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

    // A user-initiated navigation would leave the rendered document (external
    // site, relative file, missing anchor, …) and the WebView would show an
    // error/warning page, destroying the preview. Cancel it and explain instead.
    private void OnPreviewNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (!e.IsUserInitiated)
            return;

        e.Cancel = true;

        var uri = e.Uri ?? string.Empty;
        var hashIndex = uri.IndexOf('#');
        // The preview document is loaded via NavigateToString (about:blank) but
        // carries <base href="https://mdlocal/">, so an in-page anchor resolves
        // to either form. Anything else (external site, relative file, …) is a
        // real navigation away from the rendered document.
        var isFragmentJump = hashIndex >= 0 &&
            (hashIndex == 0 || uri[..hashIndex] is "about:blank" or "https://mdlocal/");

        var message = isFragmentJump
            ? $"The anchor '#{Uri.UnescapeDataString(uri[(hashIndex + 1)..])}' does not exist on this page."
            : $"This link cannot be followed inside the preview:\n\n{uri}\n\nOnly in-page anchor links (#heading) are allowed here.";

        ThemedMessageBox.Show(Application.Current.MainWindow, message,
            "Link not allowed", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true;
        ThemedMessageBox.Show(Application.Current.MainWindow,
            $"This link cannot be followed inside the preview:\n\n{e.Uri}",
            "Link not allowed", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    // Reverse sync: the preview template posts {type:'mdline', line:n} when a
    // rendered block is clicked; forward it as a typed event.
    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var json = System.Text.Json.JsonDocument.Parse(e.WebMessageAsJson);
            if (json.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object &&
                json.RootElement.TryGetProperty("type", out var type) && type.GetString() == "mdline" &&
                json.RootElement.TryGetProperty("line", out var line) && line.TryGetInt32(out var value) && value > 0)
                SourceLineRequested?.Invoke(value);
        }
        catch
        {
            // Foreign messages are ignored.
        }
    }
}
