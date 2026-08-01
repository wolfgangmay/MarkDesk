using System.IO;
using System.Windows;
using System.Windows.Controls;
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
        WebView.CoreWebView2InitializationCompleted += OnCoreWebView2Ready;
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

        if (_initializing)
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
        }
        catch (Exception ex)
        {
            LoadingHint.Text = "WebView2 unavailable: " + ex.Message;
            _initializing = false;
            throw;
        }
    }

    private void OnCoreWebView2Ready(object? sender, CoreWebView2InitializationCompletedEventArgs e)
    {
        if (!e.IsSuccess)
        {
            LoadingHint.Text = "WebView2 init failed";
            _initializing = false;
            return;
        }

        _initialized = true;
        _initializing = false;
        WebView.CoreWebView2.Settings.AreDevToolsEnabled = false;
        WebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;

        var html = _pendingHtml;
        var folder = _pendingFolder;
        _pendingHtml = null;
        _pendingFolder = null;

        if (html != null)
            _ = ApplyAsync(html, folder);
    }

    private async Task ApplyAsync(string html, string? documentFolder)
    {
        EnsureFolderMapping(documentFolder);
        WebView.CoreWebView2.NavigateToString(html);
        LoadingHint.Visibility = Visibility.Collapsed;
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
}
