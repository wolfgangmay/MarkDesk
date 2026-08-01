namespace MarkDesk.Services;

public interface IDialogService
{
    string? PickOpenMarkdownFile();
    string? PickSaveMarkdownFile(string? currentPath);
    string? PickSavePdfFile(string? currentPath);
    FileReloadChoice AskReloadExternalChange();
    bool AskConfirm(string message, string title);
    UnsavedChoice AskUnsavedChanges();
    void Warn(string message, string title);
}

public enum FileReloadChoice
{
    Reload,
    KeepMine,
    Cancel
}

public enum UnsavedChoice
{
    Save,
    DontSave,
    Cancel
}
