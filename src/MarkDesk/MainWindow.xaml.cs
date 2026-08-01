using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using MarkDesk.Models;
using MarkDesk.Services;
using MarkDesk.ViewModels;

namespace MarkDesk;

public partial class MainWindow : Window
{
    public static readonly RoutedUICommand CycleViewModeCommand = new(
        "Cycle View Mode", "CycleViewMode", typeof(MainWindow));

    private readonly DispatcherTimer _debounceTimer;
    private bool _previewVisible;
    private bool _tabbedShowPreview;

    public MainViewModel ViewModel { get; }

    public MainWindow(MainViewModel viewModel, IDialogService dialogService)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = ViewModel;

        _debounceTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(Math.Max(50, viewModel.RenderDebounceMs)),
            DispatcherPriority.Background,
            (_, _) => { _debounceTimer.Stop(); RenderNow(); },
            Dispatcher.CurrentDispatcher);

        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        SizeChanged += (_, _) => ApplyLayout();
        Loaded += (_, _) => ApplyLayout();
        Closing += OnClosing;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModel.ViewMode))
        {
            ApplyLayout();
        }
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
        PreviewStatus.Text = showPreview ? "Preview: synced ✓" : "Preview: idle";
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
        Preview.Visibility = showPreview ? Visibility.Visible : Visibility.Collapsed;
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

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (ViewModel.IsDirty && ViewModel.AskUnsavedOnClose() == UnsavedChoice.Cancel)
            e.Cancel = true;
    }

    private void New_Executed(object sender, ExecutedRoutedEventArgs e) => ViewModel.NewDocumentCommand.Execute(null);
    private void Open_Executed(object sender, ExecutedRoutedEventArgs e) => ViewModel.OpenCommand.Execute(null);
    private void Save_Executed(object sender, ExecutedRoutedEventArgs e) => ViewModel.SaveCommand.Execute(null);
    private void SaveAs_Executed(object sender, ExecutedRoutedEventArgs e) => ViewModel.SaveAsCommand.Execute(null);
    private void ExportPdf_Executed(object sender, ExecutedRoutedEventArgs e) { }
    private void Exit_Executed(object sender, ExecutedRoutedEventArgs e) => Close();
    private void CycleViewMode_Executed(object sender, ExecutedRoutedEventArgs e) => ViewModel.CycleViewModeCommand.Execute(null);

    private void Window_PreviewDragOver(object sender, DragEventArgs e)
        => e.Effects = IsMarkdownDrop(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;

    private void Window_Drop(object sender, DragEventArgs e)
    {
        var path = GetDroppedMarkdownPath(e.Data);
        if (path != null)
            ViewModel.OpenPath(path);
    }

    private static bool IsMarkdownDrop(IDataObject data) => GetDroppedMarkdownPath(data) != null;

    private static string? GetDroppedMarkdownPath(IDataObject data)
    {
        if (!data.GetDataPresent(DataFormats.FileDrop))
            return null;
        if (data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0)
            return null;
        var path = files[0];
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".md" or ".markdown" ? path : null;
    }
}
