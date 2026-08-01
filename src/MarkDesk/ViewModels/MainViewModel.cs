using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarkDesk.Models;
using MarkDesk.Services;

namespace MarkDesk.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;

    public MainViewModel(ISettingsService settingsService)
    {
        _settingsService = settingsService;
        ViewMode = _settingsService.Current.DefaultViewMode;
        UpdateTitle();
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
    private int _caretLine = 1;

    [ObservableProperty]
    private int _caretColumn = 1;

    [ObservableProperty]
    private string _encoding = "UTF-8";

    [ObservableProperty]
    private int _wordCount;

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

    partial void OnFilePathChanged(string? value) => UpdateTitle();
    partial void OnIsDirtyChanged(bool value) => UpdateTitle();

    partial void OnViewModeChanged(ViewMode value)
    {
        OnPropertyChanged(nameof(IsEditActive));
        OnPropertyChanged(nameof(IsSplitActive));
        OnPropertyChanged(nameof(IsPreviewActive));
    }

    private void UpdateTitle()
    {
        var name = FilePath is null ? "Untitled" : Path.GetFileName(FilePath);
        var dirty = IsDirty ? " ●" : string.Empty;
        Title = $"MarkDesk — {name}{dirty}";
    }
}
