using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MarkDesk.Models;
using MarkDesk.Services;
using MarkDesk.ViewModels;
using MarkDesk.Views;
using Microsoft.Extensions.DependencyInjection;

namespace MarkDesk;

public partial class MainWindow : Window
{
    public static readonly RoutedUICommand CycleViewModeCommand = new(
        "Cycle View Mode", "CycleViewMode", typeof(MainWindow));

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".svg", ".ico"
    };

    private readonly DispatcherTimer _debounceTimer;
    private readonly IDialogService _dialogService;
    private readonly IImagePasterService _imagePaster;
    private readonly FileWatcher _fileWatcher;

    private bool _previewVisible;
    private bool _tabbedShowPreview;

    public MainViewModel ViewModel { get; }

    public MainWindow(MainViewModel viewModel, IDialogService dialogService, IImagePasterService imagePaster)
    {
        InitializeComponent();
        ViewModel = viewModel;
        _dialogService = dialogService;
        _imagePaster = imagePaster;
        _fileWatcher = new FileWatcher();
        DataContext = ViewModel;

        _debounceTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(Math.Max(50, viewModel.RenderDebounceMs))
        };
        _debounceTimer.Tick += (_, _) => { _debounceTimer.Stop(); RenderNow(); };

        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        SizeChanged += (_, _) => ApplyLayout();
        Loaded += (_, _) =>
        {
            ApplyTheme(ViewModel.IsPreviewDark);
            ApplyLayout();
            _fileWatcher.Watch(ViewModel.FilePath);
            PopulateRecent();
            UpdateZoomLabel();
        };
        Preview.ZoomChanged += (_, _) => UpdateZoomLabel();
        PreviewKeyDown += OnPreviewKeyDown;
        Editor.ScrollChanged += OnEditorScroll;
        _fileWatcher.ExternalChanged += (_, _) => Dispatcher.BeginInvoke(OnExternalChange);
        Closing += OnClosing;
    }

    private void OnEditorScroll(object? sender, EventArgs e)
    {
        if (!ViewModel.ScrollSync || !_previewVisible)
            return;
        _ = Preview.SetScrollProportionAsync(Editor.ScrollProportion);
    }

    private void OnExternalChange()
    {
        if (ViewModel.FilePath == null)
            return;

        if (!ViewModel.IsDirty)
        {
            ViewModel.ReloadCurrent();
            return;
        }

        var choice = _dialogService.AskReloadExternalChange();
        if (choice == FileReloadChoice.Reload)
            ViewModel.ReloadCurrent();
        else if (choice == FileReloadChoice.KeepMine)
            ViewModel.IsDirty = true;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModel.ViewMode))
            ApplyLayout();
        else if (e.PropertyName == nameof(ViewModel.IsPreviewDark))
            ApplyTheme(ViewModel.IsPreviewDark);
        else if (e.PropertyName == nameof(ViewModel.DocumentText) && _previewVisible)
        {
            _debounceTimer.Stop();
            _debounceTimer.Start();
        }
        else if (e.PropertyName == nameof(ViewModel.FilePath))
            _fileWatcher.Watch(ViewModel.FilePath);
    }

    private enum LayoutState { EditOnly, PreviewOnly, SplitWide, SplitTabbed }

    private static SolidColorBrush Brush(byte r, byte g, byte b) => new(Color.FromRgb(r, g, b));

    private void ApplyTheme(bool dark)
    {
        if (dark)
        {
            Resources["WindowBgBrush"] = Brush(0x1E, 0x1E, 0x1E);
            Resources["BarBgBrush"] = Brush(0x25, 0x25, 0x26);
            Resources["ToneBgBrush"] = Brush(0x2D, 0x2D, 0x2D);
            Resources["ContentBrush"] = Brush(0xE6, 0xE6, 0xE6);
            Resources["MutedBrush"] = Brush(0x9A, 0x9A, 0x9A);
            Resources["DividerBrush"] = Brush(0x3F, 0x3F, 0x46);
            Resources["HoverBrush"] = Brush(0x3A, 0x3A, 0x3A);
            Resources["PressedBrush"] = Brush(0x4A, 0x4A, 0x4A);
            Resources["AccentBrush"] = Brush(0x4A, 0xA3, 0xE3);
            Resources["AccentSoftBrush"] = Brush(0x1E, 0x3A, 0x5F);
            Resources["AccentTextBrush"] = Brush(0x6C, 0xB6, 0xF4);
        }
        else
        {
            Resources["WindowBgBrush"] = Brush(0xFF, 0xFF, 0xFF);
            Resources["BarBgBrush"] = Brush(0xFF, 0xFF, 0xFF);
            Resources["ToneBgBrush"] = Brush(0xF3, 0xF3, 0xF3);
            Resources["ContentBrush"] = Brush(0x1F, 0x1F, 0x1F);
            Resources["MutedBrush"] = Brush(0x6E, 0x6E, 0x6E);
            Resources["DividerBrush"] = Brush(0xE5, 0xE5, 0xE5);
            Resources["HoverBrush"] = Brush(0xF0, 0xF0, 0xF0);
            Resources["PressedBrush"] = Brush(0xE0, 0xE0, 0xE0);
            Resources["AccentBrush"] = Brush(0x00, 0x67, 0xC0);
            Resources["AccentSoftBrush"] = Brush(0xE8, 0xF1, 0xFB);
            Resources["AccentTextBrush"] = Brush(0x00, 0x5F, 0xB8);
        }

        Editor.ApplyTheme(dark);
        if (_previewVisible)
            RenderNow();
    }

    private void ThemeToggle_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.ThemeMode = ViewModel.IsPreviewDark ? Models.ThemeMode.Light : Models.ThemeMode.Dark;
    }

    private void ApplyLayout()
    {
        if (!IsLoaded)
            return;

        var state = ComputeState();

        var showEditor = state switch
        {
            LayoutState.EditOnly => true,
            LayoutState.PreviewOnly => false,
            LayoutState.SplitWide => true,
            LayoutState.SplitTabbed => !_tabbedShowPreview,
            _ => true
        };
        var showPreview = state switch
        {
            LayoutState.EditOnly => false,
            LayoutState.PreviewOnly => true,
            LayoutState.SplitWide => true,
            LayoutState.SplitTabbed => _tabbedShowPreview,
            _ => false
        };

        TabStrip.Visibility = state == LayoutState.SplitTabbed ? Visibility.Visible : Visibility.Collapsed;
        if (state == LayoutState.SplitTabbed)
        {
            TabEdit.IsChecked = !_tabbedShowPreview;
            TabPreview.IsChecked = _tabbedShowPreview;
        }

        SetColumns(showEditor, showPreview);

        if (showPreview && !_previewVisible)
            RenderNow();

        _previewVisible = showPreview;
        if (_previewVisible && PreviewStatus.Text != "Exporting PDF…")
            PreviewStatus.Text = "Preview: synced ✓";
    }

    private void SetColumns(bool showEditor, bool showPreview)
    {
        var star = new GridLength(1, GridUnitType.Star);
        var zero = new GridLength(0);

        EditorCol.Width = showEditor ? star : zero;
        PreviewCol.Width = showPreview ? star : zero;
        var both = showEditor && showPreview;
        SplitterCol.Width = both ? GridLength.Auto : zero;
        Splitter.Visibility = both ? Visibility.Visible : Visibility.Collapsed;
        Editor.Visibility = showEditor ? Visibility.Visible : Visibility.Collapsed;
        Preview.Visibility = Visibility.Visible;
    }

    private LayoutState ComputeState()
    {
        var mode = ViewModel.ViewMode;
        var wide = ActualWidth >= ViewModel.LayoutThresholdPx;
        if (mode == ViewMode.Edit)
            return LayoutState.EditOnly;
        if (mode == ViewMode.Preview)
            return LayoutState.PreviewOnly;
        return wide ? LayoutState.SplitWide : LayoutState.SplitTabbed;
    }

    private void RenderNow()
    {
        _debounceTimer.Stop();
        try
        {
            var html = ViewModel.BuildPreviewDocument();
            _ = Preview.UpdateAsync(html, ViewModel.DocumentFolder);
            PreviewStatus.Text = "Preview: synced ✓";
        }
        catch (Exception ex)
        {
            PreviewStatus.Text = "Preview: error — " + ex.Message;
        }
    }

    private void TabEdit_Click(object sender, RoutedEventArgs e)
    {
        _tabbedShowPreview = false;
        ApplyLayout();
        Editor.FocusEditor();
    }

    private void TabPreview_Click(object sender, RoutedEventArgs e)
    {
        _tabbedShowPreview = true;
        ApplyLayout();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.D0 && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            Preview.ResetZoom();
            UpdateZoomLabel();
            return;
        }
        if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control && Clipboard.ContainsImage())
        {
            e.Handled = true;
            _ = PasteImageAsync(null, ".png");
        }
    }

    private async Task PasteImageAsync(byte[]? bytes, string extension)
    {
        if (ViewModel.FilePath == null)
        {
            ViewModel.SaveAsCommand.Execute(null);
            if (ViewModel.FilePath == null)
                return;
        }

        try
        {
            bytes ??= EncodeToPng(Clipboard.GetImage());
            if (bytes == null)
                return;
            var result = _imagePaster.SaveImage(bytes, extension, ViewModel.FilePath);
            Editor.InsertAtCaret(result.MarkdownLink);
        }
        catch (Exception ex)
        {
            _dialogService.Warn(ex.Message, "Paste image");
        }
    }

    private static byte[]? EncodeToPng(BitmapSource? source)
    {
        if (source == null)
            return null;
        using var ms = new MemoryStream();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        encoder.Save(ms);
        return ms.ToArray();
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var svm = App.Services.GetRequiredService<SettingsViewModel>();
        var dialog = new SettingsDialog(svm, (int)ActualWidth) { Owner = this };
        dialog.ShowDialog();

        ViewModel.ThemeMode = svm.ThemeMode;
        _debounceTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(50, ViewModel.RenderDebounceMs));
        ApplyLayout();
    }

    private void PopulateRecent()
    {
        RecentMenu.Items.Clear();
        var recent = ViewModel.RecentFiles;
        if (recent.Count == 0)
        {
            RecentMenu.Items.Add(new MenuItem { Header = "(empty)", IsEnabled = false });
            return;
        }

        foreach (var path in recent)
        {
            var captured = path;
            var item = new MenuItem { Header = Path.GetFileName(path), ToolTip = path };
            item.Click += (_, _) => ViewModel.OpenPath(captured);
            RecentMenu.Items.Add(item);
        }

        RecentMenu.Items.Add(new Separator());
        var clear = new MenuItem { Header = "Clear list" };
        clear.Click += (_, _) => { ViewModel.ClearRecent(); PopulateRecent(); };
        RecentMenu.Items.Add(clear);
    }

    private void UpdateZoomLabel()
    {
        ZoomLabel.Text = $"{(int)Math.Round(Preview.PreviewZoom * 100)}%";
    }

    private void Recent_SubmenuOpened(object sender, RoutedEventArgs e) => PopulateRecent();

    private void Find_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.ViewMode == ViewMode.Preview)
            ViewModel.ViewMode = ViewMode.Edit;
        Editor.ShowSearch();
    }

    private void Replace_Click(object sender, RoutedEventArgs e) => Find_Click(sender, e);

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (ViewModel.IsDirty && ViewModel.AskUnsavedOnClose() == UnsavedChoice.Cancel)
            e.Cancel = true;
        else
            _fileWatcher.Dispose();
    }

    private void New_Executed(object sender, ExecutedRoutedEventArgs e) => ViewModel.NewDocumentCommand.Execute(null);
    private void Open_Executed(object sender, ExecutedRoutedEventArgs e) => ViewModel.OpenCommand.Execute(null);
    private void Save_Executed(object sender, ExecutedRoutedEventArgs e) { _fileWatcher.NotifySelfSave(); ViewModel.SaveCommand.Execute(null); }
    private void SaveAs_Executed(object sender, ExecutedRoutedEventArgs e) { _fileWatcher.NotifySelfSave(); ViewModel.SaveAsCommand.Execute(null); }

    private async void ExportPdf_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        var path = _dialogService.PickSavePdfFile(ViewModel.FilePath);
        if (path == null)
            return;

        PreviewStatus.Text = "Exporting PDF…";
        try
        {
            var html = ViewModel.BuildPreviewDocument();
            var ok = await Preview.PrintToPdfAsync(html, ViewModel.DocumentFolder, path, ViewModel.PdfPageSize);
            PreviewStatus.Text = ok ? "PDF exported ✓" : "PDF export failed";
            if (!ok)
                _dialogService.Warn("PDF export failed.", "Export");
        }
        catch (Exception ex)
        {
            PreviewStatus.Text = "PDF error";
            _dialogService.Warn("PDF export failed:\n" + ex.Message, "Export");
        }
    }

    private void Exit_Executed(object sender, ExecutedRoutedEventArgs e) => Close();
    private void CycleViewMode_Executed(object sender, ExecutedRoutedEventArgs e) => ViewModel.CycleViewModeCommand.Execute(null);

    private void Window_PreviewDragOver(object sender, DragEventArgs e)
        => e.Effects = IsMarkdownDrop(e.Data) || IsImageDrop(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop) &&
            e.Data.GetData(DataFormats.FileDrop) is string[] files &&
            files.Length > 0)
        {
            var path = files[0];
            var ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext is ".md" or ".markdown")
            {
                ViewModel.OpenPath(path);
            }
            else if (ImageExtensions.Contains(ext))
            {
                var bytes = File.ReadAllBytes(path);
                await PasteImageAsync(bytes, ext);
            }
        }
    }

    private static bool IsMarkdownDrop(IDataObject data) => GetDroppedPath(data, ".md", ".markdown") != null;
    private static bool IsImageDrop(IDataObject data) => GetDroppedPath(data, ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".svg", ".ico") != null;

    private static string? GetDroppedPath(IDataObject data, params string[] allowedExtensions)
    {
        if (!data.GetDataPresent(DataFormats.FileDrop))
            return null;
        if (data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0)
            return null;
        var ext = Path.GetExtension(files[0]).ToLowerInvariant();
        return Array.IndexOf(allowedExtensions, ext) >= 0 ? files[0] : null;
    }
}
