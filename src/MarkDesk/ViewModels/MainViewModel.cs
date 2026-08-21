using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Utils;
using MarkDesk.Models;
using MarkDesk.Services;

namespace MarkDesk.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;
    private readonly IFileService _fileService;
    private readonly IDialogService _dialogService;
    private readonly IMarkdownRenderer _markdownRenderer;
    private readonly PreviewTemplate _previewTemplate;
    private readonly RenderGate _renderGate = new();
    private CancellationTokenSource? _renderCts;

    private DetectedEncoding _currentEncoding = new(new UTF8Encoding(false), "UTF-8", false);
    private bool _isLoading;

    public MainViewModel(
        ISettingsService settingsService,
        IFileService fileService,
        IDialogService dialogService,
        IMarkdownRenderer markdownRenderer,
        PreviewTemplate previewTemplate)
    {
        _settingsService = settingsService;
        _fileService = fileService;
        _dialogService = dialogService;
        _markdownRenderer = markdownRenderer;
        _previewTemplate = previewTemplate;
        ViewMode = _settingsService.Current.DefaultViewMode;
        ThemeMode = _settingsService.Current.ThemeMode;
        Encoding = _currentEncoding.DisplayName;
        UpdateTitle();
        UpdateWordCount();
    }

    [ObservableProperty]
    private string _title = "MarkDesk";

    [ObservableProperty]
    private string? _filePath;

    [ObservableProperty]
    private bool _isDirty;

    [ObservableProperty]
    private ViewMode _viewMode;

    [ObservableProperty]
    private ThemeMode _themeMode;

    public bool IsPreviewDark => ThemeService.IsDark(ThemeMode);

    [ObservableProperty]
    private int _caretLine = 1;

    [ObservableProperty]
    private int _caretColumn = 1;

    private string _encoding = "UTF-8";
    public string Encoding
    {
        get => _encoding;
        set => SetProperty(ref _encoding, value);
    }

    private string _documentText = string.Empty;
    public string DocumentText
    {
        get => _documentText;
        set
        {
            // Cancel any in-flight render of the OLD text BEFORE the change
            // notification: the notification may synchronously start the next
            // render (document switch renders immediately), and cancelling
            // after the notification would kill that just-started render.
            _renderCts?.Cancel();
            if (SetProperty(ref _documentText, value))
            {
                if (!_isLoading)
                    IsDirty = true;
                UpdateWordCount();
            }
        }
    }

    /// <summary>Document size in bytes (file length, or UTF-8 estimate for unsaved text).</summary>
    [ObservableProperty]
    private long _documentBytes;

    /// <summary>Size tier — drives debounce, preview mode and read-only state.</summary>
    [ObservableProperty]
    private DocumentTier _documentTier = DocumentTier.RealTime;

    [ObservableProperty]
    private int _wordCount;

    public int LayoutThresholdPx => _settingsService.Current.LayoutThresholdPx;
    public int RenderDebounceMs => _settingsService.Current.RenderDebounceMs;
    public PdfPageSize PdfPageSize => _settingsService.Current.PdfPageSize;
    public PdfMargins PdfMargins => _settingsService.Current.PdfMargins;
    public int EditorFontSize => _settingsService.Current.EditorFontSize;
    public bool TypingAssists => _settingsService.Current.TypingAssists;

    public void PersistEditorFontSize(double size)
    {
        _settingsService.Current.EditorFontSize = (int)Math.Clamp(Math.Round(size), 8, 36);
        _settingsService.Save();
    }
    public bool ScrollSync => _settingsService.Current.ScrollSync;
    public IReadOnlyList<string> RecentFiles => _settingsService.Current.RecentFiles;

    public void AddRecent(string path)
    {
        var list = _settingsService.Current.RecentFiles;
        list.Remove(path);
        list.Insert(0, path);
        while (list.Count > 10)
            list.RemoveAt(list.Count - 1);
        _settingsService.Save();
        OnPropertyChanged(nameof(RecentFiles));
    }

    public void ClearRecent()
    {
        _settingsService.Current.RecentFiles.Clear();
        _settingsService.Save();
        OnPropertyChanged(nameof(RecentFiles));
    }

    public string? DocumentFolder =>
        string.IsNullOrEmpty(FilePath) ? null : Path.GetDirectoryName(FilePath);

    public string BuildPreviewDocument()
    {
        var body = _markdownRenderer.RenderToHtml(DocumentText);
        return _previewTemplate.Build(body, IsPreviewDark);
    }

    // Render cache, keyed by text reference: theme flips and view-mode
    // toggles re-render byte-identical content, and the outline needs the
    // same syntax tree the preview just parsed. The AST is held weakly so a
    // multi-MB document's tree is never pinned in memory (outline falls
    // back to its own parse once the GC collects it).
    private string? _renderText;
    private string? _renderBody;
    private WeakReference<Markdig.Syntax.MarkdownDocument>? _renderAst;
    private string? _renderPage;
    private bool _renderPageDark;

    /// <summary>
    /// Background preview render: cancels any in-flight render, runs the
    /// Markdig pass off the UI thread, and returns null when a newer render
    /// owns the preview (version gate) or the render was cancelled.
    /// </summary>
    public async Task<string?> BuildPreviewDocumentAsync()
    {
        _renderCts?.Cancel();
        var cts = _renderCts = new CancellationTokenSource();
        var version = _renderGate.Next();
        try
        {
            var text = DocumentText;
            string body;
            if (ReferenceEquals(text, _renderText) && _renderBody != null)
            {
                // Same text as the last render (view-mode toggle, layout
                // change): reuse the body, skip the multi-second parse.
                body = _renderBody;
            }
            else
            {
                var (html, ast) = await Task.Run(() =>
                {
                    // Parse once for both the HTML and the outline (B2).
                    var doc = _markdownRenderer.Parse(text);
                    return (_markdownRenderer.RenderToHtml(doc, cts.Token), doc);
                }, cts.Token);
                _renderText = text;
                _renderBody = body = html;
                _renderAst = new WeakReference<Markdig.Syntax.MarkdownDocument>(ast);
                _renderPage = null; // body changed → page cache is stale
            }
            if (cts.IsCancellationRequested || !_renderGate.TryClaim(version))
                return null;
            if (_renderPage == null || _renderPageDark != IsPreviewDark)
            {
                _renderPage = _previewTemplate.Build(body, IsPreviewDark);
                _renderPageDark = IsPreviewDark;
            }
            return _renderPage;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>
    /// Heading outline. Tier 3 (large file) uses the fast line scan instead
    /// of a full Markdig parse — the cost difference is seconds vs hundreds
    /// of milliseconds at 5+ MB. Other tiers reuse the preview's parse when
    /// it is still alive (weak reference).
    /// </summary>
    public IReadOnlyList<OutlineItem> BuildOutline()
    {
        if (DocumentTier == DocumentTier.Large)
            return FastOutlineScanner.Extract(DocumentText);

        if (ReferenceEquals(DocumentText, _renderText) &&
            _renderAst is { } weak &&
            weak.TryGetTarget(out var ast))
        {
            return OutlineParser.Extract(ast);
        }
        return OutlineParser.Extract(_markdownRenderer.Parse(DocumentText));
    }

    // PDF export always uses light theme regardless of the app's current theme,
    // so the printed document is consistently readable on paper.
    public string BuildPdfDocument()
    {
        // Reference-equal text ⇒ the cached body is byte-identical; skips a
        // multi-second parse when the preview already rendered this content.
        var body = ReferenceEquals(DocumentText, _renderText) && _renderBody != null
            ? _renderBody
            : _markdownRenderer.RenderToHtml(DocumentText);
        return _previewTemplate.Build(body, dark: false);
    }

    public bool IsEditActive
    {
        get => ViewMode == ViewMode.Edit;
        set { if (value) ViewMode = ViewMode.Edit; }
    }

    public bool IsSplitActive
    {
        get => ViewMode == ViewMode.Split;
        set { if (value) ViewMode = ViewMode.Split; }
    }

    public bool IsPreviewActive
    {
        get => ViewMode == ViewMode.Preview;
        set { if (value) ViewMode = ViewMode.Preview; }
    }

    [RelayCommand]
    private void CycleViewMode()
    {
        ViewMode = ViewMode switch
        {
            ViewMode.Edit => ViewMode.Split,
            ViewMode.Split => ViewMode.Preview,
            _ => ViewMode.Edit
        };
    }

    [RelayCommand]
    private async Task NewDocumentAsync()
    {
        if (!await EnsureSavedAsync())
            return;
        SetDocument(string.Empty, null, new DetectedEncoding(new UTF8Encoding(false), "UTF-8", false));
    }

    [RelayCommand]
    private async Task OpenAsync()
    {
        if (!await EnsureSavedAsync())
            return;
        var path = _dialogService.PickOpenMarkdownFile();
        if (path != null)
            await LoadFromAsync(path);
    }

    public async Task OpenPathAsync(string path, bool keepViewMode = false)
    {
        if (!File.Exists(path))
            return;
        // Normalize to an absolute path: relative paths (e.g. from command line)
        // would make DocumentFolder relative, which WebView2's virtual-host
        // mapping rejects — silently breaking rendering.
        path = Path.GetFullPath(path);
        if (!await EnsureSavedAsync())
            return;
        await LoadFromAsync(path, keepViewMode);
    }

    public async Task ReloadCurrentAsync()
    {
        if (FilePath == null)
            return;
        try
        {
            OpenProgressText = "Reloading file…";
            var result = await Task.Run(() => _fileService.Load(FilePath));
            OpenProgressText = "Building editor…";
            var document = await BuildDocumentAsync(result.Text);
            SetDocument(result.Text, FilePath, result.Encoding, document);
            OpenProgressText = null;
        }
        catch (Exception ex)
        {
            OpenProgressText = null;
            _dialogService.Warn("Failed to reload file:\n" + ex.Message, "Reload");
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (FilePath == null)
        {
            await SaveAsAsync();
            return;
        }
        await DoSaveToAsync(FilePath);
    }

    [RelayCommand]
    private async Task SaveAsAsync()
    {
        var path = _dialogService.PickSaveMarkdownFile(FilePath);
        if (path == null)
            return;
        if (await DoSaveToAsync(path))
            FilePath = path;
    }

    partial void OnIsDirtyChanged(bool value) => UpdateTitle();
    partial void OnFilePathChanged(string? value) => UpdateTitle();

    partial void OnThemeModeChanged(ThemeMode value)
    {
        _settingsService.Current.ThemeMode = value;
        _settingsService.Save();
        OnPropertyChanged(nameof(IsPreviewDark));
    }

    partial void OnViewModeChanged(ViewMode value)
    {
        OnPropertyChanged(nameof(IsEditActive));
        OnPropertyChanged(nameof(IsSplitActive));
        OnPropertyChanged(nameof(IsPreviewActive));
    }

    public async Task LoadFromAsync(string path, bool keepViewMode = false)
    {
        var info = new FileInfo(path);
        if (info.Exists && DocumentTierResolver.ForBytes(info.Length) == DocumentTier.Large)
        {
            var mb = info.Length / (1024 * 1024);
            if (!_dialogService.AskConfirm(
                    $"This file is large ({mb} MB) and will open in large-file mode:\n\n" +
                    "• Preview disabled\n• Read-only editing\n• Fast heading outline\n\nContinue opening?",
                    "Large file mode"))
                return;
        }

        try
        {
            // Read + decode + rope build all off the UI thread; the window
            // stays responsive while a large document loads.
            OpenProgressText = "Reading file…";
            var result = await Task.Run(() => _fileService.Load(path));
            OpenProgressText = "Building editor…";
            var document = await BuildDocumentAsync(result.Text);
            SetDocument(result.Text, path, result.Encoding, document);
            OpenProgressText = null;
            AddRecent(path);
            // Large-file mode has no preview: always force the editor,
            // overriding the user's view. Otherwise the first document
            // (double-click launch / explicit open) goes to the default view
            // while later opens from the files panel keep the current mode.
            if (DocumentTier == DocumentTier.Large)
                ViewMode = ViewMode.Edit;
            else if (!keepViewMode)
                ViewMode = _settingsService.Current.DefaultViewMode;
        }
        catch (Exception ex)
        {
            OpenProgressText = null;
            _dialogService.Warn($"Failed to open file:\n{ex.Message}", "Open error");
        }
    }

    /// <summary>
    /// Builds the rope off the UI thread. The TextDocument itself must be
    /// created on the UI thread: AvalonEdit text documents are thread-affine
    /// ("text document cannot be accessed only from the thread that owns it"
    /// in debug builds), so only the rope work is backgrounded.
    /// </summary>
    private static async Task<TextDocument> BuildDocumentAsync(string text)
    {
        var rope = await Task.Run(() => new Rope<char>(text ?? string.Empty));
        return new TextDocument(new RopeTextSource(rope));
    }

    // In-flight background writes. The semaphore serializes the actual disk
    // writes across ALL save entry points (Save / SaveAs / EnsureSaved): each
    // write is atomic (GUID temp + overwrite Move), but two concurrent writes
    // could land out of order — the older snapshot's Move arriving last would
    // leave stale content on disk while the dirty flag was already cleared.
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly List<Task> _inFlightSaves = new();

    /// <summary>Raised on the UI thread after a save finishes writing to disk.</summary>
    public event Action? SaveWriteCompleted;

    /// <summary>True while at least one save is writing on a background thread.</summary>
    public bool IsSaveInFlight
    {
        get
        {
            _inFlightSaves.RemoveAll(t => t.IsCompleted);
            return _inFlightSaves.Count > 0;
        }
    }

    /// <summary>Completes when every save started so far has finished writing.</summary>
    public Task AllSavesCompletedAsync()
    {
        _inFlightSaves.RemoveAll(t => t.IsCompleted);
        return Task.WhenAll(_inFlightSaves);
    }

    private async Task<bool> DoSaveToAsync(string path)
    {
        // Snapshot on the UI thread: the write runs on the thread pool while
        // the user may keep typing.
        var text = DocumentText;
        var encoding = _currentEncoding.Encoding;

        Exception? error = null;
        var write = Task.Run(async () =>
        {
            // Serialized: a queued save waits here so an older snapshot can
            // never overwrite a newer one that landed first.
            await _writeLock.WaitAsync();
            try
            {
                _fileService.Save(path, text, encoding);
                return true;
            }
            catch (Exception ex)
            {
                error = ex; // dialog must be shown on the UI thread, not here
                return false;
            }
            finally
            {
                _writeLock.Release();
            }
        });
        _inFlightSaves.Add(write);
        var ok = await write;

        SaveWriteCompleted?.Invoke(); // caller re-arms the FileWatcher ignore window AFTER the write lands

        if (!ok)
        {
            _dialogService.Warn($"Failed to save file:\n{error?.Message}", "Save error");
            return false;
        }

        // Clear the dirty flag only when nothing was edited while the
        // write was in flight: every DocumentText push produces a new
        // string reference, so a reference check is sufficient.
        if (ReferenceEquals(DocumentText, text))
            IsDirty = false;
        UpdateTitle();
        try
        {
            AddRecent(path); // persists settings.json — must not take the app down on IO errors
        }
        catch
        {
            // Recent-list persistence is best-effort; the document itself is saved.
        }
        return ok;
    }

    private void SetDocument(string text, string? path, DetectedEncoding encoding, TextDocument? document = null)
    {
        _isLoading = true;
        try
        {
            // Free the previous document's cached render (multi-MB bodies
            // shouldn't linger after a file switch). Reference-keyed lookups
            // would miss anyway; this just releases memory promptly.
            _renderText = null;
            _renderBody = null;
            _renderPage = null;
            _renderAst = null;

            // Tier BEFORE DocumentText: the word-count update triggered by
            // DocumentText checks the tier (skips the multi-MB split in
            // large-file mode). With the old order a newly opened LARGE
            // document was word-counted on the UI thread, and switching from
            // a large file to a normal one kept the stale count (tier still
            // Large when the text changed).
            DocumentBytes = path != null && File.Exists(path)
                ? new FileInfo(path).Length
                : System.Text.Encoding.UTF8.GetByteCount(text);
            DocumentTier = DocumentTierResolver.ForBytes(DocumentBytes);

            // PendingDocument first, then FilePath: MainWindow consumes the
            // pre-built rope on the FilePath change and syncs DocumentText.
            // Assigning DocumentText first would make MarkdownEditor do a
            // full Document.Text replacement of the previous (possibly 6 MB)
            // document on the UI thread.
            PendingDocument = document;
            FilePath = path;
            DocumentText = text;
            _currentEncoding = encoding;
            Encoding = encoding.DisplayName;
            IsDirty = false;
        }
        finally
        {
            _isLoading = false;
        }
        UpdateTitle();
    }

    /// <summary>
    /// Pre-built rope document for the editor (large-file path); consumed by
    /// MainWindow once the editor is ready, then cleared.
    /// </summary>
    public TextDocument? PendingDocument { get; set; }

    /// <summary>Non-null while a large document is being opened or parsed; shown in the status bar.</summary>
    [ObservableProperty]
    private string? _openProgressText;

    /// <summary>
    /// True while SetDocument is assigning a freshly loaded document. Lets the
    /// window distinguish a document SWITCH (render immediately) from typing
    /// (render on the typing debounce — waiting it out after every file open
    /// made the preview visibly lag the outline).
    /// </summary>
    public bool IsLoadingDocument => _isLoading;

    private async Task<bool> EnsureSavedAsync()
    {
        if (!IsDirty)
            return true;

        var choice = _dialogService.AskUnsavedChanges();
        return choice switch
        {
            UnsavedChoice.Save => await DoSaveForEnsureAsync(),
            UnsavedChoice.DontSave => true,
            _ => false
        };
    }

    public async Task<UnsavedChoice> AskUnsavedOnCloseAsync()
    {
        if (!IsDirty)
            return UnsavedChoice.DontSave;

        var choice = _dialogService.AskUnsavedChanges();
        if (choice == UnsavedChoice.Save)
        {
            if (FilePath == null)
            {
                var path = _dialogService.PickSaveMarkdownFile(FilePath);
                if (path == null)
                    return UnsavedChoice.Cancel;
                if (!await DoSaveToAsync(path))
                    return UnsavedChoice.Cancel;
                FilePath = path;
            }
            else
            {
                if (!await DoSaveToAsync(FilePath))
                    return UnsavedChoice.Cancel;
            }
            return UnsavedChoice.Save;
        }
        return choice;
    }

    private async Task<bool> DoSaveForEnsureAsync()
    {
        if (FilePath == null)
        {
            var path = _dialogService.PickSaveMarkdownFile(FilePath);
            if (path == null)
                return false;
            if (!await DoSaveToAsync(path))
                return false;
            FilePath = path;
            return true;
        }
        return await DoSaveToAsync(FilePath);
    }

    private void UpdateTitle()
    {
        var name = FilePath is null ? "Untitled" : Path.GetFileName(FilePath);
        var dirty = IsDirty ? " ●" : string.Empty;
        Title = $"MarkDesk — {name}{dirty}";
    }

    private void UpdateWordCount()
    {
        // Skipped in large-file mode: scanning 5+ MB per change is wasted work.
        if (DocumentTier == DocumentTier.Large)
            return;
        WordCount = CountWords(DocumentText.AsSpan());
    }

    /// <summary>
    /// Allocation-free word count. Matches the semantics of
    /// <c>text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length</c>
    /// (whitespace-separated runs) without materializing an array of substrings.
    /// </summary>
    private static int CountWords(ReadOnlySpan<char> text)
    {
        var count = 0;
        var inWord = false;
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c))
                inWord = false;
            else if (!inWord)
            {
                inWord = true;
                count++;
            }
        }
        return count;
    }
}
