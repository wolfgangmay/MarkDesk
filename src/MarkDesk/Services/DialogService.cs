using System.Windows;
using MarkDesk.Controls;
using Microsoft.Win32;

namespace MarkDesk.Services;

public sealed class DialogService : IDialogService
{
    private const string MarkdownFilter = "Markdown (*.md;*.markdown)|*.md;*.markdown|All files (*.*)|*.*";

    private static Window? ActiveOwner => Application.Current.MainWindow;

    public string? PickOpenMarkdownFile()
    {
        var dlg = new OpenFileDialog
        {
            Filter = MarkdownFilter,
            Title = "Open Markdown"
        };
        return dlg.ShowDialog(ActiveOwner) == true ? dlg.FileName : null;
    }

    public string? PickSaveMarkdownFile(string? currentPath)
    {
        var dlg = new SaveFileDialog
        {
            Filter = MarkdownFilter,
            Title = "Save Markdown",
            FileName = string.IsNullOrEmpty(currentPath) ? "Untitled.md" : System.IO.Path.GetFileName(currentPath),
            DefaultExt = ".md",
            AddExtension = true
        };
        return dlg.ShowDialog(ActiveOwner) == true ? dlg.FileName : null;
    }

    public string? PickSavePdfFile(string? currentPath)
    {
        var dlg = new SaveFileDialog
        {
            Filter = "PDF (*.pdf)|*.pdf",
            Title = "Export as PDF",
            FileName = string.IsNullOrEmpty(currentPath)
                ? "Untitled.pdf"
                : System.IO.Path.GetFileNameWithoutExtension(currentPath) + ".pdf",
            DefaultExt = ".pdf",
            AddExtension = true
        };
        return dlg.ShowDialog(ActiveOwner) == true ? dlg.FileName : null;
    }

    public FileReloadChoice AskReloadExternalChange()
    {
        var result = ThemedMessageBox.Show(ActiveOwner,
            "This file has been changed by another program.\nReload it?",
            "File changed", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        return result switch
        {
            MessageBoxResult.Yes => FileReloadChoice.Reload,
            MessageBoxResult.No => FileReloadChoice.KeepMine,
            _ => FileReloadChoice.Cancel
        };
    }

    public bool AskConfirm(string message, string title) =>
        ThemedMessageBox.Show(ActiveOwner, message, title, MessageBoxButton.YesNo, MessageBoxImage.Question)
            == MessageBoxResult.Yes;

    public UnsavedChoice AskUnsavedChanges()
    {
        var result = ThemedMessageBox.Show(ActiveOwner,
            "You have unsaved changes. Save before continuing?",
            "Unsaved changes", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
        return result switch
        {
            MessageBoxResult.Yes => UnsavedChoice.Save,
            MessageBoxResult.No => UnsavedChoice.DontSave,
            _ => UnsavedChoice.Cancel
        };
    }

    public void Warn(string message, string title) =>
        ThemedMessageBox.Show(ActiveOwner, message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
}
