using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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

    private bool _previewVisible;
    private bool _tabbedShowPreview;

    public MainViewModel ViewModel { get; }

    public MainWindow(MainViewModel viewModel, IDialogService dialogService, IImagePasterService imagePaster)
    {
        InitializeComponent();
        ViewModel = viewModel;
        _dialogService = dialogService;
        _imagePaster = imagePaster;
        DataContext = ViewModel;

        _debounceTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(Math.Max(50, viewModel.RenderDebounceMs))
        };
        _debounceTimer.Tick += (_, _) => { _debounceTimer.Stop(); RenderNow(); };

        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        SizeChanged += (_, _) => ApplyLayout();
        Loaded += (_, _) => ApplyLayout();
        PreviewKeyDown += OnPreviewKeyDown;
        Closing += OnClosing;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModel.ViewMode))
            ApplyLayout();
        else if (e.PropertyName == nameof(ViewModel.DocumentText) && _previewVisible)
        {
            _debounceTimer.Stop();
            _debounceTimer.Start();
        }
    }

    private enum LayoutState { EditOnly, PreviewOnly, SplitWide, SplitTabbed }

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

        _debounceTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(50, ViewModel.RenderDebounceMs));
        ApplyLayout();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (ViewModel.IsDirty && ViewModel.AskUnsavedOnClose() == UnsavedChoice.Cancel)
            e.Cancel = true;
    }

    private void New_Executed(object sender, ExecutedRoutedEventArgs e) => ViewModel.NewDocumentCommand.Execute(null);
    private void Open_Executed(object sender, ExecutedRoutedEventArgs e) => ViewModel.OpenCommand.Execute(null);
    private void Save_Executed(object sender, ExecutedRoutedEventArgs e) => ViewModel.SaveCommand.Execute(null);
    private void SaveAs_Executed(object sender, ExecutedRoutedEventArgs e) => ViewModel.SaveAsCommand.Execute(null);

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
