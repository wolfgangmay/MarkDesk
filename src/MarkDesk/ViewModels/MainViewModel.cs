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
            if (SetProperty(ref _documentText, value))
            {
                // Any in-flight render is now stale; cancel it so a slow
                // background render can't compete with the next keystroke.
                _renderCts?.Cancel();
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

    /// <summary>
    /// Background preview render: cancels any in-flight render, runs the
    /// Markdig pass off the UI thread, and returns null when a newer render
    /// owns the preview (version gate) or the render was cancelled.
    /// </summary>
    public async Task<string?> BuildPreviewDocumentAsync()
    {
        _renderCts?.Cancel();
        _renderCts?.Dispose();
        var cts = _renderCts = new CancellationTokenSource();
        var version = _renderGate.Next();
        try
        {
            var body = await Task.Run(
                () => _markdownRenderer.RenderToHtml(DocumentText, cts.Token), cts.Token);
            if (cts.IsCancellationRequested || !_renderGate.TryClaim(version))
                return null;
            return _previewTemplate.Build(body, IsPreviewDark);
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>
    /// Heading outline. Tier 3 (large file) uses the fast line scan instead
    /// of a full Markdig parse — the cost difference is seconds vs hundreds
    /// of milliseconds at 5+ MB.
    /// </summary>
    public IReadOnlyList<OutlineItem> BuildOutline() =>
        DocumentTier == DocumentTier.Large
            ? FastOutlineScanner.Extract(DocumentText)
            : OutlineParser.Extract(_markdownRenderer.Parse(DocumentText));

    // PDF export always uses light theme regardless of the app's current theme,
    // so the printed document is consistently readable on paper.
    public string BuildPdfDocument()
    {
        var body = _markdownRenderer.RenderToHtml(DocumentText);
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
    private void NewDocument()
    {
        if (!EnsureSaved())
            return;
        SetDocument(string.Empty, null, new DetectedEncoding(new UTF8Encoding(false), "UTF-8", false));
    }

    [RelayCommand]
    private async Task OpenAsync()
    {
        if (!EnsureSaved())
            return;
        var path = _dialogService.PickOpenMarkdownFile();
        if (path != null)
            await LoadFromAsync(path);
    }

    public void OpenPath(string path, bool keepViewMode = false)
    {
        if (!File.Exists(path))
            return;
        // Normalize to an absolute path: relative paths (e.g. from command line)
        // would make DocumentFolder relative, which WebView2's virtual-host
        // mapping rejects — silently breaking rendering.
        path = Path.GetFullPath(path);
        if (!EnsureSaved())
            return;
        _ = LoadFromAsync(path, keepViewMode);
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
    private void Save()
    {
        if (FilePath == null)
        {
            SaveAs();
            return;
        }
        DoSaveTo(FilePath);
    }

    [RelayCommand]
    private void SaveAs()
    {
        var path = _dialogService.PickSaveMarkdownFile(FilePath);
        if (path == null)
            return;
        if (DoSaveTo(path))
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

    private bool DoSaveTo(string path)
    {
        try
        {
            _fileService.Save(path, DocumentText, _currentEncoding.Encoding);
            IsDirty = false;
            UpdateTitle();
            AddRecent(path);
            return true;
        }
        catch (Exception ex)
        {
            _dialogService.Warn($"Failed to save file:\n{ex.Message}", "Save error");
            return false;
        }
    }

    private void SetDocument(string text, string? path, DetectedEncoding encoding, TextDocument? document = null)
    {
        _isLoading = true;
        try
        {
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

    private bool EnsureSaved()
    {
        if (!IsDirty)
            return true;

        var choice = _dialogService.AskUnsavedChanges();
        return choice switch
        {
            UnsavedChoice.Save => DoSaveForEnsure(),
            UnsavedChoice.DontSave => true,
            _ => false
        };
    }

    public UnsavedChoice AskUnsavedOnClose()
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
                if (!DoSaveTo(path))
                    return UnsavedChoice.Cancel;
                FilePath = path;
            }
            else
            {
                if (!DoSaveTo(FilePath))
                    return UnsavedChoice.Cancel;
            }
            return UnsavedChoice.Save;
        }
        return choice;
    }

    private bool DoSaveForEnsure()
    {
        if (FilePath == null)
        {
            var path = _dialogService.PickSaveMarkdownFile(FilePath);
            if (path == null)
                return false;
            if (!DoSaveTo(path))
                return false;
            FilePath = path;
            return true;
        }
        return DoSaveTo(FilePath);
    }

    private void UpdateTitle()
    {
        var name = FilePath is null ? "Untitled" : Path.GetFileName(FilePath);
        var dirty = IsDirty ? " ●" : string.Empty;
        Title = $"MarkDesk — {name}{dirty}";
    }

    private void UpdateWordCount()
    {
        // Skipped in large-file mode: word-splitting 5+ MB allocates heavily.
        if (DocumentTier == DocumentTier.Large)
            return;
        WordCount = string.IsNullOrWhiteSpace(DocumentText)
            ? 0
            : DocumentText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
    }
}
