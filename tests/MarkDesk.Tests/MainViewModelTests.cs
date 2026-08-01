using System.IO;
using System.Text;
using MarkDesk.Models;
using MarkDesk.Services;
using MarkDesk.ViewModels;

namespace MarkDesk.Tests;

internal sealed class FakeFileService : IFileService
{
    public Func<string, DocumentLoadResult>? OnLoad;
    public Action<string, string, Encoding>? OnSave;

    public DocumentLoadResult Load(string filePath) =>
        OnLoad?.Invoke(filePath) ?? new DocumentLoadResult(string.Empty, new DetectedEncoding(new UTF8Encoding(false), "UTF-8", false));

    public void Save(string filePath, string content, Encoding encoding) =>
        OnSave?.Invoke(filePath, content, encoding);
}

internal sealed class FakeDialogService : IDialogService
{
    public string? OpenPath { get; set; }
    public string? SavePath { get; set; }
    public UnsavedChoice Unsaved { get; set; } = UnsavedChoice.DontSave;
    public bool Confirm { get; set; } = true;
    public int WarnCount { get; private set; }

    public string? PickOpenMarkdownFile() => OpenPath;
    public string? PickSaveMarkdownFile(string? currentPath) => SavePath;
    public FileReloadChoice AskReloadExternalChange() => FileReloadChoice.KeepMine;
    public bool AskConfirm(string message, string title) => Confirm;
    public UnsavedChoice AskUnsavedChanges() => Unsaved;
    public void Warn(string message, string title) => WarnCount++;
}

public class MainViewModelTests
{
    private static MainViewModel Create(FakeFileService? file = null, FakeDialogService? dialog = null)
    {
        var settings = new SettingsService(Path.Combine(Path.GetTempPath(), "MarkDeskVM_" + Guid.NewGuid().ToString("N")));
        return new MainViewModel(settings, file ?? new FakeFileService(), dialog ?? new FakeDialogService());
    }

    [Fact]
    public void NewDocument_ClearsContentAndDirty()
    {
        var vm = Create();
        vm.DocumentText = "something";
        Assert.True(vm.IsDirty);

        vm.NewDocumentCommand.Execute(null);

        Assert.Equal(string.Empty, vm.DocumentText);
        Assert.False(vm.IsDirty);
        Assert.Null(vm.FilePath);
    }

    [Fact]
    public void OpenPath_LoadsTextAndEncoding()
    {
        var path = Path.Combine(Path.GetTempPath(), "MarkDeskOpenPath_" + Guid.NewGuid().ToString("N") + ".md");
        File.WriteAllText(path, "placeholder");
        var file = new FakeFileService
        {
            OnLoad = _ => new DocumentLoadResult("# Hello", new DetectedEncoding(Encoding.GetEncoding(936), "GBK", false))
        };
        var vm = Create(file);

        try
        {
            vm.OpenPath(path);

            Assert.Equal("# Hello", vm.DocumentText);
            Assert.Equal(path, vm.FilePath);
            Assert.Equal("GBK", vm.Encoding);
            Assert.False(vm.IsDirty);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void Typing_SetsDirty_AndUpdatesWordCount()
    {
        var vm = Create();

        vm.DocumentText = "one two three";

        Assert.True(vm.IsDirty);
        Assert.Equal(3, vm.WordCount);
    }

    [Fact]
    public void Save_WhenFilePathNull_PromptsAndWrites()
    {
        var savedPath = "";
        var savedContent = "";
        var file = new FakeFileService
        {
            OnSave = (p, c, _) => { savedPath = p; savedContent = c; }
        };
        var dialog = new FakeDialogService { SavePath = "C:\\fake\\out.md" };
        var vm = Create(file, dialog);
        vm.DocumentText = "draft content";

        vm.SaveCommand.Execute(null);

        Assert.Equal("C:\\fake\\out.md", savedPath);
        Assert.Equal("draft content", savedContent);
        Assert.False(vm.IsDirty);
        Assert.Equal("C:\\fake\\out.md", vm.FilePath);
    }

    [Fact]
    public void Save_WhenFilePathSet_WritesWithoutPrompt()
    {
        var saveCalls = 0;
        var file = new FakeFileService { OnSave = (_, _, _) => saveCalls++ };
        var dialog = new FakeDialogService { SavePath = "C:\\should-not-be-used.md" };
        var vm = Create(file, dialog);
        vm.OpenPath("C:\\fake\\existing.md");
        vm.DocumentText = "changed";

        vm.SaveCommand.Execute(null);

        Assert.Equal(1, saveCalls);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void Open_BlockedWhenUnsavedCancel()
    {
        var file = new FakeFileService { OnLoad = _ => new DocumentLoadResult("loaded", new DetectedEncoding(new UTF8Encoding(false), "UTF-8", false)) };
        var dialog = new FakeDialogService { OpenPath = "C:\\fake\\doc.md", Unsaved = UnsavedChoice.Cancel };
        var vm = Create(file, dialog);
        vm.DocumentText = "unsaved";

        vm.OpenCommand.Execute(null);

        Assert.NotEqual("loaded", vm.DocumentText);
        Assert.True(vm.IsDirty);
    }

    [Fact]
    public void AskUnsavedOnClose_Save_PersistsAndReportsSaved()
    {
        var file = new FakeFileService();
        var dialog = new FakeDialogService { SavePath = "C:\\fake\\out.md", Unsaved = UnsavedChoice.Save };
        var vm = Create(file, dialog);
        vm.DocumentText = "to save";

        var result = vm.AskUnsavedOnClose();

        Assert.Equal(UnsavedChoice.Save, result);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public void AskUnsavedOnClose_Cancel_DoesNotPersist()
    {
        var dialog = new FakeDialogService { Unsaved = UnsavedChoice.Cancel };
        var vm = Create(dialog: dialog);
        vm.DocumentText = "to keep";

        var result = vm.AskUnsavedOnClose();

        Assert.Equal(UnsavedChoice.Cancel, result);
        Assert.True(vm.IsDirty);
    }
}
