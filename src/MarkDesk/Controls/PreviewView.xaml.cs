using System.IO;
using System.Windows;
using System.Windows.Controls;
using MarkDesk.Models;
using Microsoft.Web.WebView2.Core;

namespace MarkDesk.Controls;

public partial class PreviewView : UserControl
{
    private const string VirtualHost = "mdlocal";

    private string? _pendingHtml;
    private string? _pendingFolder;
    private string? _mappedFolder;
    private bool _initialized;
    private bool _initializing;

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

        if (!_initializing)
            await EnsureInitializedAsync();
    }

    public async Task<bool> PrintToPdfAsync(string html, string? documentFolder, string outputPath, PdfPageSize pageSize)
    {
        await EnsureInitializedAsync();
        EnsureFolderMapping(documentFolder);

        var navDone = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(object? s, CoreWebView2NavigationCompletedEventArgs e)
        {
            WebView.CoreWebView2.NavigationCompleted -= Handler;
            navDone.TrySetResult(e.IsSuccess);
        }

        WebView.CoreWebView2.NavigationCompleted += Handler;
        WebView.CoreWebView2.NavigateToString(html);

        if (!await navDone.Task)
            return false;

        var settings = WebView.CoreWebView2.Environment.CreatePrintSettings();
        settings.ShouldPrintBackgrounds = true;
        settings.ShouldPrintHeaderAndFooter = false;
        ApplyPageSize(settings, pageSize);

        await WebView.CoreWebView2.PrintToPdfAsync(outputPath, settings);
        return true;
    }

    private async Task EnsureInitializedAsync()
    {
        if (_initialized)
            return;

        _initializing = true;
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
        finally
        {
            _initializing = false;
        }
    }

    private async Task ApplyAsync(string html, string? documentFolder)
    {
        EnsureFolderMapping(documentFolder);
        WebView.CoreWebView2.NavigateToString(html);
        await Task.CompletedTask;
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
    }

    private static void ApplyPageSize(CoreWebView2PrintSettings settings, PdfPageSize pageSize)
    {
        settings.PageWidth = pageSize == PdfPageSize.Letter ? 8.5 : 8.27;
        settings.PageHeight = pageSize == PdfPageSize.Letter ? 11.0 : 11.69;
    }
}
