using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Input;
using MarkDesk.Services;
using MarkDesk.ViewModels;

namespace MarkDesk;

public partial class MainWindow : Window
{
    public static readonly RoutedUICommand CycleViewModeCommand = new(
        "Cycle View Mode", "CycleViewMode", typeof(MainWindow));

    public MainViewModel ViewModel { get; }

    public MainWindow(MainViewModel viewModel, IDialogService dialogService)
    {
        InitializeComponent();
        ViewModel = viewModel;
        DataContext = ViewModel;

        Editor.CaretPositionChanged += (_, _) =>
        {
            ViewModel.CaretLine = Editor.CaretLine;
            ViewModel.CaretColumn = Editor.CaretColumn;
        };

        Closing += OnClosing;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (ViewModel.IsDirty)
        {
            var choice = ViewModel.AskUnsavedOnClose();
            if (choice == UnsavedChoice.Cancel)
                e.Cancel = true;
        }
    }

    private void New_Executed(object sender, ExecutedRoutedEventArgs e) => ViewModel.NewDocumentCommand.Execute(null);
    private void Open_Executed(object sender, ExecutedRoutedEventArgs e) => ViewModel.OpenCommand.Execute(null);
    private void Save_Executed(object sender, ExecutedRoutedEventArgs e) => ViewModel.SaveCommand.Execute(null);
    private void SaveAs_Executed(object sender, ExecutedRoutedEventArgs e) => ViewModel.SaveAsCommand.Execute(null);
    private void ExportPdf_Executed(object sender, ExecutedRoutedEventArgs e) { }
    private void Exit_Executed(object sender, ExecutedRoutedEventArgs e) => Close();
    private void CycleViewMode_Executed(object sender, ExecutedRoutedEventArgs e) => ViewModel.CycleViewModeCommand.Execute(null);

    private void Window_PreviewDragOver(object sender, DragEventArgs e) => e.Effects = IsMarkdownDrop(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;

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
