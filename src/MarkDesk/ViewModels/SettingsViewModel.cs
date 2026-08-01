using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarkDesk.Models;
using MarkDesk.Services;

namespace MarkDesk.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settingsService;

    public SettingsViewModel(ISettingsService settingsService)
    {
        _settingsService = settingsService;
        Load();
    }

    public IReadOnlyList<ViewMode> ViewModes { get; } = Enum.GetValues<ViewMode>().ToArray();
    public IReadOnlyList<PdfPageSize> PageSizes { get; } = Enum.GetValues<PdfPageSize>().ToArray();
    public IReadOnlyList<ThemeMode> ThemeModes { get; } = Enum.GetValues<ThemeMode>().ToArray();

    [ObservableProperty] private int _layoutThresholdPx;
    [ObservableProperty] private ViewMode _defaultViewMode;
    [ObservableProperty] private ThemeMode _themeMode;
    [ObservableProperty] private string _assetsFolderName = "assets";
    [ObservableProperty] private string _imageNamePattern = "";
    [ObservableProperty] private bool _scrollSync;
    [ObservableProperty] private int _renderDebounceMs;
    [ObservableProperty] private PdfPageSize _pdfPageSize;

    public int CurrentWindowWidth { get; set; }

    public void Load()
    {
        var s = _settingsService.Current;
        LayoutThresholdPx = s.LayoutThresholdPx;
        DefaultViewMode = s.DefaultViewMode;
        ThemeMode = s.ThemeMode;
        AssetsFolderName = s.AssetsFolderName;
        ImageNamePattern = s.ImageNamePattern;
        ScrollSync = s.ScrollSync;
        RenderDebounceMs = s.RenderDebounceMs;
        PdfPageSize = s.PdfPageSize;
    }

    [RelayCommand]
    public void Save()
    {
        var s = _settingsService.Current;
        s.LayoutThresholdPx = LayoutThresholdPx;
        s.DefaultViewMode = DefaultViewMode;
        s.ThemeMode = ThemeMode;
        s.AssetsFolderName = AssetsFolderName;
        s.ImageNamePattern = ImageNamePattern;
        s.ScrollSync = ScrollSync;
        s.RenderDebounceMs = RenderDebounceMs;
        s.PdfPageSize = PdfPageSize;
        _settingsService.Save();
    }
}
