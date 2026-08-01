using System.IO;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarkDesk.Models;
using MarkDesk.Services;

namespace MarkDesk.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private const long LargeFileThresholdBytes = 5L * 1024 * 1024; // 5 MB (FR-01)

    private readonly ISettingsService _settingsService;
    private readonly IFileService _fileService;
    private readonly IDialogService _dialogService;
    private readonly IMarkdownRenderer _markdownRenderer;
    private readonly PreviewTemplate _previewTemplate;

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
                if (!_isLoading)
                    IsDirty = true;
                UpdateWordCount();
            }
        }
    }

    [ObservableProperty]
    private int _wordCount;

    public int LayoutThresholdPx => _settingsService.Current.LayoutThresholdPx;
    public int RenderDebounceMs => _settingsService.Current.RenderDebounceMs;
    public PdfPageSize PdfPageSize => _settingsService.Current.PdfPageSize;
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
    private void Open()
    {
        if (!EnsureSaved())
            return;
        var path = _dialogService.PickOpenMarkdownFile();
        if (path != null)
            LoadFrom(path);
    }

    public void OpenPath(string path)
    {
        if (!File.Exists(path))
            return;
        if (!EnsureSaved())
            return;
        LoadFrom(path);
    }

    public void ReloadCurrent()
    {
        if (FilePath == null)
            return;
        try
        {
            var result = _fileService.Load(FilePath);
            SetDocument(result.Text, FilePath, result.Encoding);
        }
        catch (Exception ex)
        {
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
        DoSaveTo(path);
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

    private void LoadFrom(string path)
    {
        var info = new FileInfo(path);
        if (info.Exists && info.Length > LargeFileThresholdBytes)
        {
            var mb = info.Length / (1024 * 1024);
            if (!_dialogService.AskConfirm(
                    $"This file is large ({mb} MB). Continue opening?",
                    "Large file"))
                return;
        }

        try
        {
            var result = _fileService.Load(path);
            SetDocument(result.Text, path, result.Encoding);
            AddRecent(path);
        }
        catch (Exception ex)
        {
            _dialogService.Warn($"Failed to open file:\n{ex.Message}", "Open error");
        }
    }

    private void DoSaveTo(string path)
    {
        try
        {
            _fileService.Save(path, DocumentText, _currentEncoding.Encoding);
            IsDirty = false;
            UpdateTitle();
            AddRecent(path);
        }
        catch (Exception ex)
        {
            _dialogService.Warn($"Failed to save file:\n{ex.Message}", "Save error");
        }
    }

    private void SetDocument(string text, string? path, DetectedEncoding encoding)
    {
        _isLoading = true;
        try
        {
            DocumentText = text;
            FilePath = path;
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
                DoSaveTo(path);
                FilePath = path;
            }
            else
            {
                DoSaveTo(FilePath);
            }
            return IsDirty ? UnsavedChoice.Cancel : UnsavedChoice.Save;
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
            DoSaveTo(path);
            FilePath = path;
            return !IsDirty;
        }
        DoSaveTo(FilePath);
        return !IsDirty;
    }

    private void UpdateTitle()
    {
        var name = FilePath is null ? "Untitled" : Path.GetFileName(FilePath);
        var dirty = IsDirty ? " ●" : string.Empty;
        Title = $"MarkDesk — {name}{dirty}";
    }

    private void UpdateWordCount()
    {
        WordCount = string.IsNullOrWhiteSpace(DocumentText)
            ? 0
            : DocumentText.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
    }
}
