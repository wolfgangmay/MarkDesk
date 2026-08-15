using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MarkDesk.Controls;
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

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int cbAttribute);

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".svg", ".ico"
    };

    private readonly DispatcherTimer _debounceTimer;
    private readonly DispatcherTimer _outlineTimer;
    private readonly IDialogService _dialogService;
    private readonly IImagePasterService _imagePaster;
    private readonly FileWatcher _fileWatcher;

    private bool _previewVisible;
    private bool _tabbedShowPreview;
    private bool _outlineWanted = true;

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

        _outlineTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(300) };
        _outlineTimer.Tick += (_, _) => { _outlineTimer.Stop(); UpdateOutline(); };

        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        SizeChanged += (_, _) => ApplyLayout();
        Loaded += (_, _) =>
        {
            ApplyTheme(ViewModel.IsPreviewDark);
            ApplyLayout();
            Editor.SetFontSize(ViewModel.EditorFontSize);
            Editor.TypingAssistsEnabled = ViewModel.TypingAssists;
            var s = App.Services.GetRequiredService<Services.ISettingsService>().Current;
            _outlineWanted = s.OutlineVisible;
            OutlineToggle.IsChecked = _outlineWanted;
            OutlineCol.Width = new GridLength(Math.Clamp(s.OutlineWidthPx, 160, 480));
            ApplyLayout();
            UpdateOutline();
            _fileWatcher.Watch(ViewModel.FilePath);
            PopulateRecent();
            UpdateZoomLabel();
            RegisterSubmenuHandlers();
        };
        Preview.ZoomChanged += (_, _) => UpdateZoomLabel();
        Editor.ZoomChanged += OnEditorZoomChanged;
        PreviewKeyDown += OnPreviewKeyDown;
        Editor.ScrollChanged += OnEditorScroll;
        Editor.CaretPositionChanged += (_, _) =>
        {
            ViewModel.CaretLine = Editor.CaretLine;
            ViewModel.CaretColumn = Editor.CaretColumn;
            if (Outline.Visibility == Visibility.Visible)
                Outline.HighlightLine(Editor.CaretLine);
        };
        Outline.HeadingClicked += OnOutlineHeadingClicked;
        OutlineSplitter.DragCompleted += OutlineSplitter_DragCompleted;
        Preview.SourceLineRequested += OnPreviewSourceLineRequested;
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
        else if (e.PropertyName == nameof(ViewModel.DocumentText))
        {
            if (_previewVisible)
            {
                _debounceTimer.Stop();
                _debounceTimer.Start();
            }
            _outlineTimer.Stop();
            _outlineTimer.Start();
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
            Application.Current.Resources["WindowBgBrush"] = Brush(0x1E, 0x1E, 0x1E);
            Application.Current.Resources["BarBgBrush"] = Brush(0x25, 0x25, 0x26);
            Application.Current.Resources["ToneBgBrush"] = Brush(0x2D, 0x2D, 0x2D);
            Application.Current.Resources["ContentBrush"] = Brush(0xE6, 0xE6, 0xE6);
            Application.Current.Resources["MutedBrush"] = Brush(0x9A, 0x9A, 0x9A);
            Application.Current.Resources["DividerBrush"] = Brush(0x3F, 0x3F, 0x46);
            Application.Current.Resources["HoverBrush"] = Brush(0x3A, 0x3A, 0x3A);
            Application.Current.Resources["PressedBrush"] = Brush(0x4A, 0x4A, 0x4A);
            Application.Current.Resources["AccentBrush"] = Brush(0x4A, 0xA3, 0xE3);
            Application.Current.Resources["AccentSoftBrush"] = Brush(0x1E, 0x3A, 0x5F);
            Application.Current.Resources["AccentTextBrush"] = Brush(0x6C, 0xB6, 0xF4);
            Application.Current.Resources[SystemColors.MenuBrushKey] = Brush(0x2D, 0x2D, 0x30);
            Application.Current.Resources[SystemColors.MenuTextBrushKey] = Brush(0xE6, 0xE6, 0xE6);
            Application.Current.Resources[SystemColors.MenuBarBrushKey] = Brush(0x25, 0x25, 0x26);
            Application.Current.Resources[SystemColors.HighlightBrushKey] = Brush(0x44, 0x4A, 0x52);
            Application.Current.Resources[SystemColors.ControlTextBrushKey] = Brush(0xE6, 0xE6, 0xE6);
            Application.Current.Resources[SystemColors.WindowBrushKey] = Brush(0x2D, 0x2D, 0x30);
            Application.Current.Resources[SystemColors.HighlightTextBrushKey] = Brush(0xFF, 0xFF, 0xFF);
            Application.Current.Resources["MenuPopupBrush"] = Brush(0x2D, 0x2D, 0x30);
            Application.Current.Resources["MenuPopupBorderBrush"] = Brush(0x3F, 0x3F, 0x46);
            Application.Current.Resources["ScrollThumbBrush"] = Brush(0x4A, 0x4A, 0x52);
            Application.Current.Resources["ScrollThumbHoverBrush"] = Brush(0x6B, 0x6B, 0x75);
        }
        else
        {
            Application.Current.Resources["WindowBgBrush"] = Brush(0xFF, 0xFF, 0xFF);
            Application.Current.Resources["BarBgBrush"] = Brush(0xFF, 0xFF, 0xFF);
            Application.Current.Resources["ToneBgBrush"] = Brush(0xF3, 0xF3, 0xF3);
            Application.Current.Resources["ContentBrush"] = Brush(0x1F, 0x1F, 0x1F);
            Application.Current.Resources["MutedBrush"] = Brush(0x6E, 0x6E, 0x6E);
            Application.Current.Resources["DividerBrush"] = Brush(0xE5, 0xE5, 0xE5);
            Application.Current.Resources["HoverBrush"] = Brush(0xF0, 0xF0, 0xF0);
            Application.Current.Resources["PressedBrush"] = Brush(0xE0, 0xE0, 0xE0);
            Application.Current.Resources["AccentBrush"] = Brush(0x00, 0x67, 0xC0);
            Application.Current.Resources["AccentSoftBrush"] = Brush(0xE8, 0xF1, 0xFB);
            Application.Current.Resources["AccentTextBrush"] = Brush(0x00, 0x5F, 0xB8);
            Application.Current.Resources[SystemColors.MenuBrushKey] = Brush(0xFF, 0xFF, 0xFF);
            Application.Current.Resources[SystemColors.MenuTextBrushKey] = Brush(0x1F, 0x1F, 0x1F);
            Application.Current.Resources[SystemColors.MenuBarBrushKey] = Brush(0xFF, 0xFF, 0xFF);
            Application.Current.Resources[SystemColors.HighlightBrushKey] = Brush(0xCC, 0xE4, 0xF7);
            Application.Current.Resources[SystemColors.ControlTextBrushKey] = Brush(0x1F, 0x1F, 0x1F);
            Application.Current.Resources[SystemColors.WindowBrushKey] = Brush(0xFF, 0xFF, 0xFF);
            Application.Current.Resources[SystemColors.HighlightTextBrushKey] = Brush(0x1F, 0x1F, 0x1F);
            Application.Current.Resources["MenuPopupBrush"] = Brush(0xF0, 0xF0, 0xF0);
            Application.Current.Resources["MenuPopupBorderBrush"] = Brush(0x99, 0x99, 0x99);
            Application.Current.Resources["ScrollThumbBrush"] = Brush(0xC8, 0xC8, 0xC8);
            Application.Current.Resources["ScrollThumbHoverBrush"] = Brush(0x8A, 0x8A, 0x8A);
        }

        TryApplyTitleBarDark(dark);
        Editor.ApplyTheme(dark);
        ApplyOpenSubmenus();
        if (_previewVisible)
            RenderNow();
    }

    private void TryApplyTitleBarDark(bool dark)
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero)
                return;
            var value = dark ? 1 : 0;
            // DWMWA_USE_IMMERSIVE_DARK_MODE: attr 20 (Win10 2004+), fallback 19 (1809+)
            if (DwmSetWindowAttribute(hwnd, 20, ref value, sizeof(int)) != 0)
                DwmSetWindowAttribute(hwnd, 19, ref value, sizeof(int));
        }
        catch
        {
            // Pre-Win10 1809: immersive dark mode unsupported; ignore.
        }
    }

    private void RegisterSubmenuHandlers()
    {
        foreach (var top in MainMenu.Items.OfType<MenuItem>())
            RegisterSubmenuHandler(top);
    }

    private void RegisterSubmenuHandler(MenuItem mi)
    {
        mi.SubmenuOpened -= Menu_SubmenuOpened;
        mi.SubmenuOpened += Menu_SubmenuOpened;
        foreach (var child in mi.Items.OfType<MenuItem>())
            RegisterSubmenuHandler(child);
    }

    private void Menu_SubmenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi)
            ApplySubmenuTheme(mi);
    }

    private void ApplySubmenuTheme(MenuItem mi)
    {
        if (mi.Template.FindName("SubMenuBorder", mi) is Border border)
        {
            border.Background = (Brush)Application.Current.Resources["MenuPopupBrush"];
            border.BorderBrush = (Brush)Application.Current.Resources["MenuPopupBorderBrush"];
        }
        else
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => ApplySubmenuTheme(mi)));
        }
    }

    private void ApplyOpenSubmenus()
    {
        foreach (var top in MainMenu.Items.OfType<MenuItem>())
            RefreshOpenSubmenu(top);
    }

    private void RefreshOpenSubmenu(MenuItem mi)
    {
        if (mi.IsSubmenuOpen)
            ApplySubmenuTheme(mi);
        foreach (var child in mi.Items.OfType<MenuItem>())
            RefreshOpenSubmenu(child);
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

        // The outline is a navigation aid for whichever pane is visible:
        // in Edit/Split modes it jumps to the source, in Preview-only mode it
        // scrolls the rendered document (same data-line map).
        var showOutline = _outlineWanted && (showEditor || showPreview);
        OutlineCol.Width = showOutline
            ? new GridLength(Math.Clamp(OutlineCol.Width.Value, 160, 480))
            : zero;
        OutlineSplitterCol.Width = showOutline ? GridLength.Auto : zero;
        OutlineSplitter.Visibility = showOutline ? Visibility.Visible : Visibility.Collapsed;
        Outline.Visibility = showOutline ? Visibility.Visible : Visibility.Collapsed;
        OutlineToggle.IsEnabled = showEditor || showPreview;
    }

    private void UpdateOutline()
    {
        Outline.SetHeadings(ViewModel.BuildOutline());
        Outline.HighlightLine(Editor.CaretLine);
    }

    private void OutlineToggle_Click(object sender, RoutedEventArgs e)
    {
        _outlineWanted = OutlineToggle.IsChecked == true;
        ApplyLayout();
        PersistOutlineLayout();
    }

    private void ToggleOutline()
    {
        _outlineWanted = !_outlineWanted;
        OutlineToggle.IsChecked = _outlineWanted;
        ApplyLayout();
        PersistOutlineLayout();
    }

    private void PersistOutlineLayout()
    {
        var settings = App.Services.GetRequiredService<Services.ISettingsService>();
        var s = settings.Current;
        s.OutlineVisible = _outlineWanted;
        s.OutlineWidthPx = (int)Math.Clamp(OutlineCol.Width.Value, 160, 480);
        settings.Save();
    }

    private void OutlineSplitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e) =>
        PersistOutlineLayout();

    private void OnOutlineHeadingClicked(int line)
    {
        // Preview-only layout: jump inside the rendered document (same
        // data-line map the reverse-sync click handler uses).
        if (ComputeState() == LayoutState.PreviewOnly)
        {
            _ = Preview.ScrollToLine(line);
            Outline.HighlightLine(line);
            return;
        }

        // In narrow split (tabbed) with the preview tab active, flip back to
        // the editor so the jump is visible.
        if (ComputeState() == LayoutState.SplitTabbed && _tabbedShowPreview)
        {
            _tabbedShowPreview = false;
            ApplyLayout();
        }
        Editor.ScrollToLine(line);
        Editor.FocusEditor();
        Outline.HighlightLine(line);
    }

    // Reverse sync: clicking a rendered block scrolls the editor to the
    // source line. Gated by the same ScrollSync setting as forward sync —
    // one mental model: the panes are linked or they are not.
    private void OnPreviewSourceLineRequested(int line)
    {
        if (!ViewModel.ScrollSync)
            return;
        Editor.ScrollToLine(line);
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
        if (e.Key == Key.F7 && Keyboard.Modifiers == ModifierKeys.None && OutlineToggle.IsEnabled)
        {
            e.Handled = true;
            ToggleOutline();
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
        Editor.TypingAssistsEnabled = ViewModel.TypingAssists;
        _debounceTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(50, ViewModel.RenderDebounceMs));
        ApplyLayout();
    }

    private void SetDefaultApp_Click(object sender, RoutedEventArgs e)
    {
        var ok = FileAssociationService.Register(".md", ".markdown");
        if (ok)
        {
            ThemedMessageBox.Show(this,
                "MarkDesk is registered for .md and .markdown files.\n\n" +
                "On Windows 10/11, if another app is currently the default, open\n" +
                "Settings \u2192 Apps \u2192 Default apps \u2192 MarkDesk\n" +
                "and confirm once. Also re-run this if you move the MarkDesk folder.",
                "File association", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            ThemedMessageBox.Show(this,
                "Could not register file associations.",
                "File association", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
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
        var editorPct = (int)Math.Round(Editor.EditorFontSize / 14.0 * 100);
        var previewPct = (int)Math.Round(Preview.PreviewZoom * 100);
        ZoomLabel.Text = $"Edit {editorPct}% · Preview {previewPct}%";
    }

    private void OnEditorZoomChanged(object? sender, EventArgs e)
    {
        UpdateZoomLabel();
        ViewModel.PersistEditorFontSize(Editor.EditorFontSize);
    }

    private void Recent_SubmenuOpened(object sender, RoutedEventArgs e) => PopulateRecent();

    private void Find_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (ViewModel.ViewMode == ViewMode.Preview)
            ViewModel.ViewMode = ViewMode.Edit;
        Editor.ShowSearch();
    }

    private void Replace_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        if (ViewModel.ViewMode == ViewMode.Preview)
            ViewModel.ViewMode = ViewMode.Edit;
        Editor.ShowReplace();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (ViewModel.IsDirty && ViewModel.AskUnsavedOnClose() == UnsavedChoice.Cancel)
            e.Cancel = true;
        else
        {
            _fileWatcher.Dispose();
            Preview.Dispose(); // #2: release WebView2 + Chromium children cleanly.
        }
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
            var html = ViewModel.BuildPdfDocument();
            var ok = await Preview.PrintToPdfAsync(html, ViewModel.DocumentFolder, path, ViewModel.PdfPageSize, ViewModel.PdfMargins);
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

    private void About_Click(object sender, RoutedEventArgs e)
    {
        var ver = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0";
        var msg = $"MarkDesk\nVersion {ver}\n\nA WPF Markdown editor with live preview, offline rendering (Markdown, KaTeX, Mermaid, syntax highlight), and PDF export.";
        ThemedMessageBox.Show(this, msg, "About MarkDesk", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Window_PreviewDragOver(object sender, DragEventArgs e)
        => e.Effects = IsMarkdownDrop(e.Data) || IsImageDrop(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop) &&
            e.Data.GetData(DataFormats.FileDrop) is string[] files &&
            files.Length > 0)
        {
            // A markdown file in the drop means "open this document" — it
            // replaces the current one, so any accompanying images are
            // ignored. Otherwise every dropped image is imported in order.
            var markdown = files.FirstOrDefault(f =>
                Path.GetExtension(f).ToLowerInvariant() is ".md" or ".markdown");
            if (markdown != null)
            {
                ViewModel.OpenPath(markdown);
                return;
            }

            foreach (var path in files)
            {
                var ext = Path.GetExtension(path).ToLowerInvariant();
                if (!ImageExtensions.Contains(ext))
                    continue;
                try
                {
                    var bytes = File.ReadAllBytes(path);
                    await PasteImageAsync(bytes, ext);
                }
                catch (Exception ex)
                {
                    _dialogService.Warn(ex.Message, "Drop image");
                }
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
