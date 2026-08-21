using System.IO;
using System.Text;
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

    // Navigation always serves HTML through WebResourceRequested (in-memory;
    // NavigateToString breaks on large HTML — WebView2Feedback #1355). HTML
    // bigger than this would also make the print-layout pass (querySelectorAll
    // over the whole DOM) take minutes, so it is skipped for those documents.
    private const int LargeHtmlThreshold = 3_000_000;

    // Chromium gives no progress for PrintToPdfAsync; guard against a wedged
    // renderer/print process instead of hanging the export forever.
    private static readonly TimeSpan PrintTimeout = TimeSpan.FromMinutes(10);

    private string? _pendingHtml;
    private string? _pendingFolder;
    private string? _mappedFolder;
    private bool _assetsMapped;
    private bool _initialized;
    private Task _initTask = Task.CompletedTask;
    private readonly SemaphoreSlim _navigationLock = new(1, 1);
    private double _previewZoom;

    /// <summary>
    /// URI of the navigation this control itself initiated (the current
    /// render). OnPreviewNavigationStarting lets exactly this URI through
    /// because WebView2 can report host-initiated Navigate() as
    /// user-initiated, and Source lags behind during NavigationStarting.
    /// </summary>
    private string? _programmaticNavigationUri;

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

    /// <summary>True while a render navigation is applying (scroll sync pauses during it).</summary>
    public bool IsNavigating => _navigationLock.CurrentCount == 0;

    public async Task UpdateAsync(string html, string? documentFolder)
    {
        LoadingHint.Visibility = _initialized ? Visibility.Collapsed : Visibility.Visible;
        WebView.Visibility = Visibility.Visible;

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
        // Don't spawn a fresh Chromium tree while the app is shutting down:
        // App.OnExit already terminated our WebView2 children, and a process
        // created now would outlive the app as an orphan.
        if (Application.Current is { } app && app.Dispatcher.HasShutdownStarted)
            throw new InvalidOperationException("The application is shutting down; PDF export was aborted.");

        await _printLock.WaitAsync();
        Microsoft.Web.WebView2.Wpf.WebView2? printWv = null;
        string? tempPath = null;
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

            // Temp-file navigation (memory serving measured 2.5x slower —
            // see ApplyAsync). Same rationale as the on-screen preview.
            var dir = Path.Combine(Path.GetTempPath(), "MarkDesk");
            Directory.CreateDirectory(dir);
            tempPath = Path.Combine(dir, "print-" + Guid.NewGuid().ToString("N") + ".html");
            await File.WriteAllTextAsync(tempPath, html, new UTF8Encoding(false));
            core.Navigate(new Uri(tempPath).AbsoluteUri);

            var navigation = await Task.WhenAny(navDone.Task, Task.Delay(TimeSpan.FromSeconds(30)));
            if (navigation != navDone.Task)
            {
                core.NavigationCompleted -= Completed;
                core.NavigationStarting -= Starting;
                throw new TimeoutException(
                    "The print document did not finish loading within 30 s.");
            }

            if (!await navDone.Task)
                throw new InvalidOperationException(
                    "Failed to load the document in the print view.");

            var settings = environment.CreatePrintSettings();
            settings.ShouldPrintBackgrounds = true;
            settings.ShouldPrintHeaderAndFooter = false;
            ApplyPageSize(settings, pageSize);
            ApplyMargins(settings, margins);
            if (html.Length < LargeHtmlThreshold)
                await ApplyPrintLayoutAsync(core, pageSize, margins);

            // PrintToPdfAsync reports no progress and can take minutes for
            // multi-MB documents; a hard timeout avoids hanging forever.
            var printTask = core.PrintToPdfAsync(outputPath, settings);
            var printWinner = await Task.WhenAny(printTask, Task.Delay(PrintTimeout));
            if (printWinner != printTask)
            {
                // Observe the abandoned print task: it faults when the WebView2
                // is disposed below, and an unobserved exception would later be
                // logged as a spurious crash by TaskScheduler.UnobservedTaskException.
                _ = printTask.ContinueWith(static t => _ = t.Exception, TaskScheduler.Default);
                throw new TimeoutException(
                    $"PDF export timed out after {(int)PrintTimeout.TotalMinutes} minutes. " +
                    "The document is probably too large for WebView2 to print.");
            }
            await printTask;
            return true;
        }
        finally
        {
            // Release the temporary WebView2 and its Chromium processes.
            try { printWv?.Dispose(); } catch { }
            if (printWv != null) PrintHost.Children.Remove(printWv);
            try { if (tempPath != null) File.Delete(tempPath); } catch { }
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
            // Temp-file navigation instead of NavigateToString (which fails on
            // large HTML, WebView2Feedback #1355) AND instead of
            // WebResourceRequested memory serving (measured 2.5x SLOWER on
            // multi-MB pages: the response body crosses a COM/IStream boundary
            // chunk by chunk, ~2 s for 6 MB, while file:// lets Chromium read
            // the bytes natively). The previous temp file is kept alive while
            // its page is shown and only deleted on the next render.
            try { if (_lastTempFile != null) File.Delete(_lastTempFile); } catch { }
            var dir = Path.Combine(Path.GetTempPath(), "MarkDesk");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "html-" + Guid.NewGuid().ToString("N") + ".html");
            await File.WriteAllTextAsync(path, html, new UTF8Encoding(false));
            _lastTempFile = path;
            // Tag our own navigation so OnPreviewNavigationStarting lets it
            // through: WebView2 sometimes reports host-initiated Navigate()
            // as user-initiated (observed on the first navigation at startup).
            _programmaticNavigationUri = new Uri(path).AbsoluteUri;
            WebView.CoreWebView2.Navigate(_programmaticNavigationUri);
            // Surface navigation failures instead of silently proceeding.
            if (!await WaitNavigationAsync())
                throw new TimeoutException("Preview navigation timed out after 10 s.");
            await ApplyZoomAsync();
        }
        finally
        {
            _navigationLock.Release();
        }
    }

    private string? _lastTempFile;

    /// <summary>Waits for the current navigation; false = timed out or failed.</summary>
    private async Task<bool> WaitNavigationAsync()
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
            return false;
        }
        return await navDone.Task;
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

    /// <summary>
    /// Large-file mode placeholder: hides the WebView and shows a message in
    /// the hint area. The next successful UpdateAsync restores the preview.
    /// </summary>
    public void ShowPlaceholder(string message)
    {
        WebView.Visibility = Visibility.Collapsed;
        LoadingHint.Text = message;
        LoadingHint.Visibility = Visibility.Visible;
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
                // Real heights: content-visibility would report placeholder
                // estimates for offscreen blocks and misclassify .md-keep.
                "document.documentElement.classList.add('md-measuring');" +
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
                "document.documentElement.classList.remove('md-measuring');" +
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
        try { if (_lastTempFile != null) File.Delete(_lastTempFile); } catch { }
        _lastTempFile = null;
        try { WebView.Dispose(); } catch { }
        foreach (System.Windows.UIElement child in PrintHost.Children)
            try { (child as Microsoft.Web.WebView2.Wpf.WebView2)?.Dispose(); } catch { }
    }

// A user-initiated navigation away from the preview document (external site,
// relative file, …) would show an error/warning page and destroy the preview.
// Navigations that stay on the current document are handled by
// PreviewNavigationPolicy: fragment jumps (#anchor) are allowed, self-reloads
// are cancelled quietly (the page lives in memory and is served per render).
private void OnPreviewNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (!e.IsUserInitiated)
            return;

        // Our own render navigation: WebView2 sometimes reports host-initiated
        // Navigate() as user-initiated (observed on the first navigation after
        // startup), and Source is still the previous page during this event,
        // so the policy would misclassify it as external and block the render.
        if (_programmaticNavigationUri != null &&
            string.Equals(e.Uri, _programmaticNavigationUri, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        _programmaticNavigationUri = null;

        var kind = PreviewNavigationPolicy.Classify(e.Uri, WebView.CoreWebView2.Source);
        if (kind == PreviewNavigationKind.InPageFragment)
            return;
        if (kind == PreviewNavigationKind.SelfReload)
        {
            e.Cancel = true;
            return;
        }

        e.Cancel = true;
        var uri = e.Uri ?? string.Empty;
        ThemedMessageBox.Show(Application.Current.MainWindow,
            $"This link cannot be followed inside the preview:\n\n{uri}\n\nOnly in-page anchor links (#heading) are allowed here.",
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
