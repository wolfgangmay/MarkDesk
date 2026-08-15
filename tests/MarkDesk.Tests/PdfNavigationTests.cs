using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using MarkDesk.Services;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace MarkDesk.Tests;

/// <summary>
/// WebView2 integration tests. Exactly one WPF Application is created for the
/// whole AppDomain (WPF allows only one); each test runs its window/WebView2
/// work on its own STA thread with a nested message pump (DispatcherFrame).
/// The "WpfApp" collection keeps these tests serial.
/// </summary>
[Collection("WpfApp")]
public class PdfNavigationTests
{
    private static readonly object AppLock = new();
    private static Application? s_app;

    /// <summary>Shared process-wide Application for all WPF tests.</summary>
    public static Application SharedApp => App;

    private static Application App
    {
        get
        {
            lock (AppLock)
            {
                if (s_app != null) return s_app;
                Exception? initFailure = null;
                var thread = new Thread(() =>
                {
                    try
                    {
                        lock (AppLock)
                        {
                            s_app = Application.Current ?? new Application();
                        }
                    }
                    catch (Exception ex) { initFailure = ex; }
                    finally
                    {
                        lock (AppLock) Monitor.PulseAll(AppLock);
                    }
                    if (initFailure == null)
                        Dispatcher.Run();
                });
                thread.SetApartmentState(ApartmentState.STA);
                thread.IsBackground = true;
                thread.Start();
                var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
                while (s_app == null && initFailure == null)
                {
                    var remaining = deadline - DateTime.UtcNow;
                    if (remaining <= TimeSpan.Zero)
                        throw new Xunit.Sdk.XunitException(
                            "WPF Application thread did not start within 30 s");
                    Monitor.Wait(AppLock, remaining);
                }
                if (s_app == null)
                    throw new Xunit.Sdk.XunitException(
                        "WPF Application creation failed: " + initFailure);
                return s_app;
            }
        }
    }

    private static string AssetsFolder => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "MarkDesk", "Assets", "web"));

    private static void RunOnSta(Func<Task> body)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                _ = App;
                var frame = new DispatcherFrame();
                Dispatcher.CurrentDispatcher.BeginInvoke(async () =>
                {
                    try { await body(); }
                    catch (Exception ex) { failure = ex; }
                    finally { frame.Continue = false; }
                });
                Dispatcher.PushFrame(frame);
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromMinutes(3)))
            throw new Xunit.Sdk.XunitException("WebView2 test timed out after 3 minutes");
        if (failure != null)
            throw new Xunit.Sdk.XunitException(failure.ToString());
    }

    private static async Task<WebView2> CreateWebViewAsync(Window win)
    {
        var wv = new WebView2();
        win.Content = wv;
        win.Show();
        // Unique user data dir per test: a leftover Chromium process from a
        // previous run would otherwise lock the folder forever.
        var env = await CoreWebView2Environment.CreateAsync(
            null, Path.Combine(Path.GetTempPath(), "MarkDeskTests-wv2", Guid.NewGuid().ToString("N")));
        await wv.EnsureCoreWebView2Async(env);
        wv.CoreWebView2.SetVirtualHostNameToFolderMapping(
            "mdassets", AssetsFolder, CoreWebView2HostResourceAccessKind.Allow);
        return wv;
    }

    private static async Task<bool> NavigateTempFileAsync(WebView2 wv, string html)
    {
        var core = wv.CoreWebView2;
        var dir = Path.Combine(Path.GetTempPath(), "MarkDeskTests");
        Directory.CreateDirectory(dir);
        var temp = Path.Combine(dir, "nav-" + Guid.NewGuid().ToString("N") + ".html");
        await File.WriteAllTextAsync(temp, html, new UTF8Encoding(false));

        var navDone = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        ulong targetId = 0;
        var waiting = true;
        void Starting(object? s, CoreWebView2NavigationStartingEventArgs e) { if (!waiting) return; waiting = false; targetId = e.NavigationId; }
        void Completed(object? s, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (waiting || e.NavigationId != targetId) return;
            core.NavigationCompleted -= Completed;
            core.NavigationStarting -= Starting;
            navDone.TrySetResult(e.IsSuccess);
        }
        core.NavigationStarting += Starting;
        core.NavigationCompleted += Completed;
        core.Navigate(new Uri(temp).AbsoluteUri);
        var ok = await navDone.Task;
        File.Delete(temp);
        return ok;
    }

    [Fact]
    public void TempFileNavigation_LoadsTemplateAssets() =>
        RunOnSta(async () =>
        {
            var win = new Window { Width = 640, Height = 480 };
            var wv = await CreateWebViewAsync(win);
            try
            {
                var template = new PreviewTemplate();
                var html = template.Build("<h1>Hello PDF</h1><pre><code class=\"language-csharp\">var x = 1;</code></pre>");
                Assert.True(await NavigateTempFileAsync(wv, html), "navigation failed");
                await Task.Delay(2000);

                var result = await wv.CoreWebView2.ExecuteScriptAsync(
                    "JSON.stringify({ h1: document.querySelector('h1')?.textContent, " +
                    "hljsLoaded: typeof window.hljs !== 'undefined', " +
                    "stylesheets: document.styleSheets.length })");
                Console.WriteLine("page state: " + result);
                Assert.Contains("Hello PDF", result);
                Assert.Contains("hljsLoaded", result);
                Assert.Contains("true", result);
                Assert.Contains("stylesheets", result);
            }
            finally
            {
                wv.Dispose();
                win.Close();
            }
        });

    [Fact]
    public void PrintToPdf_ProducesValidPdf() =>
        RunOnSta(async () =>
        {
            var win = new Window { Width = 640, Height = 480 };
            var wv = await CreateWebViewAsync(win);
            try
            {
                var sb = new StringBuilder();
                for (var i = 0; i < 2000; i++)
                    sb.Append($"<h2>Section {i}</h2><p>Paragraph with <strong>bold</strong> and <code>code</code> text.</p>");
                var template = new PreviewTemplate();
                Assert.True(await NavigateTempFileAsync(wv, template.Build(sb.ToString())), "navigation failed");
                await Task.Delay(1000);

                var pdfPath = Path.Combine(Path.GetTempPath(), "MarkDeskTests", "out-" + Guid.NewGuid().ToString("N") + ".pdf");
                var settings = wv.CoreWebView2.Environment.CreatePrintSettings();
                settings.ShouldPrintBackgrounds = true;
                settings.ShouldPrintHeaderAndFooter = false;
                settings.PageWidth = 8.27;
                settings.PageHeight = 11.69;
                settings.MarginTop = settings.MarginBottom = settings.MarginLeft = settings.MarginRight = 0.5;
                var ok = await wv.CoreWebView2.PrintToPdfAsync(pdfPath, settings);
                Assert.True(ok, "PrintToPdfAsync returned false");

                var info = new FileInfo(pdfPath);
                Assert.True(info.Exists && info.Length > 0, "PDF not produced");
                using var reader = new BinaryReader(File.OpenRead(pdfPath));
                var magic = new string(reader.ReadChars(5));
                Assert.Equal("%PDF-", magic);
                Console.WriteLine($"PDF produced: {info.Length} bytes, starts with {magic}");

                try { File.Delete(pdfPath); } catch { /* Chromium may still hold the handle briefly */ }
            }
            finally
            {
                wv.Dispose();
                win.Close();
            }
        });
}