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

    /// <summary>Simulates slow disk: Save blocks this long on the thread pool.</summary>
    public int SaveDelayMs;

    /// <summary>Content snapshot of every save, in order.</summary>
    public List<string> SavedContents { get; } = new();

    public DocumentLoadResult Load(string filePath) =>
        OnLoad?.Invoke(filePath) ?? new DocumentLoadResult(string.Empty, new DetectedEncoding(new UTF8Encoding(false), "UTF-8", false));

    public void Save(string filePath, string content, Encoding encoding)
    {
        if (SaveDelayMs > 0)
            Thread.Sleep(SaveDelayMs);
        SavedContents.Add(content);
        OnSave?.Invoke(filePath, content, encoding);
    }
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
    public string? PickSavePdfFile(string? currentPath) => null;
    public FileReloadChoice AskReloadExternalChange() => FileReloadChoice.KeepMine;
    public bool AskConfirm(string message, string title) => Confirm;
    public UnsavedChoice AskUnsavedChanges() => Unsaved;
    public void Warn(string message, string title) => WarnCount++;
}

internal sealed class CountingRenderer : IMarkdownRenderer
{
    public int ParseCalls { get; private set; }
    private readonly MarkdownRenderer _inner = new();

    public string RenderToHtml(string markdown) => _inner.RenderToHtml(markdown);
    public string RenderToHtml(string markdown, CancellationToken token) => _inner.RenderToHtml(markdown, token);
    public string RenderToHtml(Markdig.Syntax.MarkdownDocument document, CancellationToken token) => _inner.RenderToHtml(document, token);
    public Markdig.Syntax.MarkdownDocument Parse(string markdown)
    {
        ParseCalls++;
        return _inner.Parse(markdown);
    }
}

public class MainViewModelTests
{
    private static MainViewModel Create(FakeFileService? file = null, FakeDialogService? dialog = null, IMarkdownRenderer? renderer = null)
    {
        var settings = new SettingsService(Path.Combine(Path.GetTempPath(), "MarkDeskVM_" + Guid.NewGuid().ToString("N")));
        var template = new PreviewTemplate();
        return new MainViewModel(settings, file ?? new FakeFileService(), dialog ?? new FakeDialogService(), renderer ?? new MarkdownRenderer(), template);
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
    public async Task OpenPath_LoadsTextAndEncoding()
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
            await vm.LoadFromAsync(path);

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
    public async Task LoadFromAsync_FirstOpen_GoesToDefaultViewMode()
    {
        var path = Path.Combine(Path.GetTempPath(), "MarkDeskViewMode_" + Guid.NewGuid().ToString("N") + ".md");
        File.WriteAllText(path, "x");
        var file = new FakeFileService { OnLoad = _ => new DocumentLoadResult("x", new DetectedEncoding(new UTF8Encoding(false), "UTF-8", false)) };
        var vm = Create(file);

        try
        {
            vm.ViewMode = ViewMode.Edit;

            await vm.LoadFromAsync(path, keepViewMode: false);

            Assert.Equal(ViewMode.Preview, vm.ViewMode); // default from settings
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task LoadFromAsync_TreeClick_KeepsCurrentViewMode()
    {
        var path = Path.Combine(Path.GetTempPath(), "MarkDeskViewMode_" + Guid.NewGuid().ToString("N") + ".md");
        File.WriteAllText(path, "x");
        var file = new FakeFileService { OnLoad = _ => new DocumentLoadResult("x", new DetectedEncoding(new UTF8Encoding(false), "UTF-8", false)) };
        var vm = Create(file);

        try
        {
            vm.ViewMode = ViewMode.Edit;

            await vm.LoadFromAsync(path, keepViewMode: true);

            Assert.Equal(ViewMode.Edit, vm.ViewMode);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task SwitchingFromLargeToSmallFile_UpdatesWordCountAndTier()
    {
        // Regression: DocumentText was assigned before DocumentTier, so the
        // word-count gate (skip in Large tier) still saw the PREVIOUS
        // document's tier — a small file opened after a large one kept the
        // stale seven-digit count (and large files were counted on the UI
        // thread).
        var big = Path.Combine(Path.GetTempPath(), "MarkDeskBig_" + Guid.NewGuid().ToString("N") + ".md");
        var small = Path.Combine(Path.GetTempPath(), "MarkDeskSmall_" + Guid.NewGuid().ToString("N") + ".md");
        File.WriteAllBytes(big, new byte[6 * 1024 * 1024]); // > 5 MB → Large
        File.WriteAllText(small, "one two three four");
        var file = new FakeFileService
        {
            OnLoad = p => new DocumentLoadResult(
                p == big ? new string('x', 6 * 1024 * 1024) : "one two three four",
                new DetectedEncoding(new UTF8Encoding(false), "UTF-8", false))
        };
        var vm = Create(file);

        try
        {
            await vm.LoadFromAsync(big);
            await vm.LoadFromAsync(small);

            Assert.Equal(DocumentTier.RealTime, vm.DocumentTier);
            Assert.Equal(4, vm.WordCount);
        }
        finally
        {
            if (File.Exists(big)) File.Delete(big);
            if (File.Exists(small)) File.Delete(small);
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
    public async Task Save_WhenFilePathNull_PromptsAndWrites()
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

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal("C:\\fake\\out.md", savedPath);
        Assert.Equal("draft content", savedContent);
        Assert.False(vm.IsDirty);
        Assert.Equal("C:\\fake\\out.md", vm.FilePath);
    }

    [Fact]
    public async Task Save_WhenFilePathSet_WritesWithoutPrompt()
    {
        var saveCalls = 0;
        var file = new FakeFileService { OnSave = (_, _, _) => saveCalls++ };
        var dialog = new FakeDialogService { SavePath = "C:\\should-not-be-used.md" };
        var vm = Create(file, dialog);
        await vm.OpenPathAsync("C:\\fake\\existing.md");
        vm.DocumentText = "changed";

        await vm.SaveCommand.ExecuteAsync(null);

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
    public async Task AskUnsavedOnClose_Save_PersistsAndReportsSaved()
    {
        var file = new FakeFileService();
        var dialog = new FakeDialogService { SavePath = "C:\\fake\\out.md", Unsaved = UnsavedChoice.Save };
        var vm = Create(file, dialog);
        vm.DocumentText = "to save";

        var result = await vm.AskUnsavedOnCloseAsync();

        Assert.Equal(UnsavedChoice.Save, result);
        Assert.False(vm.IsDirty);
    }

    [Fact]
    public async Task AskUnsavedOnClose_Cancel_DoesNotPersist()
    {
        var dialog = new FakeDialogService { Unsaved = UnsavedChoice.Cancel };
        var vm = Create(dialog: dialog);
        vm.DocumentText = "to keep";

        var result = await vm.AskUnsavedOnCloseAsync();

        Assert.Equal(UnsavedChoice.Cancel, result);
        Assert.True(vm.IsDirty);
    }

    [Fact(Timeout = 30_000)]
    public async Task ConcurrentSaves_LandInDispatchOrder_NewestSnapshotWins()
    {
        // C1 regression: two saves from different entry points can be in
        // flight at once (Save + EnsureSaved). The first write is slow, the
        // second fast — without write serialization the second could land
        // and then be OVERWRITTEN by the slower first save's stale snapshot.
        var file = new FakeFileService();
        var dialog = new FakeDialogService { SavePath = "C:\\fake\\concurrent.md", Unsaved = UnsavedChoice.Save };
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstSaveStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callIndex = 0;
        file.OnSave = (_, _, _) =>
        {
            var idx = Interlocked.Increment(ref callIndex);
            if (idx == 2) // idx 1 was the FilePath setup save below
            {
                firstSaveStarted.TrySetResult();
                gate.Task.Wait(); // hold save #1 until save #2 has been dispatched
            }
        };
        var vm = Create(file, dialog);

        // Give the VM a FilePath (through the real save path) so SaveCommand
        // writes directly instead of falling into the SaveAs dialog branch —
        // a null dialog result there silently skips the save and would hang
        // the handshake below.
        vm.DocumentText = "setup";
        await vm.AskUnsavedOnCloseAsync();
        Assert.NotNull(vm.FilePath);

        vm.DocumentText = "old snapshot";
        var save1 = vm.SaveCommand.ExecuteAsync(null);

        await firstSaveStarted.Task; // save #1 is inside FileService.Save
        vm.DocumentText = "new snapshot";
        var save2 = vm.SaveCommand.ExecuteAsync(null); // queued behind the write lock
        await Task.Delay(50); // let save #2 reach the semaphore
        gate.TrySetResult();  // release save #1
        await save1;
        await save2;

        // The two racing saves ran in dispatch order and the NEWEST snapshot landed last.
        Assert.Equal(3, file.SavedContents.Count);
        Assert.Equal("old snapshot", file.SavedContents[1]);
        Assert.Equal("new snapshot", file.SavedContents[2]);
    }

    [Fact]
    public async Task Save_WritingInBackground_DoesNotBlockUiThreadEdits()
    {
        // The write runs on the thread pool with a simulated slow disk; the
        // UI side must stay free to edit (and the save must snapshot the
        // text from BEFORE the write, not the edit made during it).
        var file = new FakeFileService { SaveDelayMs = 150 };
        var dialog = new FakeDialogService { SavePath = "C:\\fake\\slow.md", Unsaved = UnsavedChoice.Save };
        var vm = Create(file, dialog);
        vm.DocumentText = "snapshot me";
        await vm.AskUnsavedOnCloseAsync(); // no-op path: not dirty-safe, set FilePath below

        // Give the VM a FilePath so Save writes without a dialog.
        var saveTask = vm.SaveCommand.ExecuteAsync(null);

        // Edit while the background write is in flight.
        vm.DocumentText = "edited during the write";
        await saveTask;

        // The snapshot (pre-edit) content was written…
        Assert.Equal("snapshot me", file.SavedContents[^1]);
        // …and the dirty flag survived because the text changed mid-save.
        Assert.True(vm.IsDirty);

        vm.DocumentText = "clean state";
        var saveTask2 = vm.SaveCommand.ExecuteAsync(null);
        await saveTask2;
        Assert.False(vm.IsDirty); // no edits during this write → flag cleared
        await vm.AllSavesCompletedAsync();
        Assert.False(vm.IsSaveInFlight);
    }

    [Fact]
    public void WordCount_MatchesSplitSemantics_ZeroAlloc()
    {
        var vm = Create();
        vm.DocumentText = "  你好 world\tfoo bar\n\nbaz  ";

        Assert.Equal(5, vm.WordCount);

        vm.DocumentText = "   \t \n ";
        Assert.Equal(0, vm.WordCount);

        vm.DocumentText = "";
        Assert.Equal(0, vm.WordCount);
    }

    [Fact]
    public async Task BuildPreviewDocumentAsync_SameText_ReusesCachedPage()
    {
        var renderer = new CountingRenderer();
        var vm = Create(renderer: renderer);
        vm.DocumentText = "# cache me";

        var page1 = await vm.BuildPreviewDocumentAsync();
        var page2 = await vm.BuildPreviewDocumentAsync();

        Assert.NotNull(page1);
        Assert.Same(page1, page2);            // no re-parse, no re-template
        Assert.Equal(1, renderer.ParseCalls); // parsed exactly once
    }

    [Fact]
    public async Task BuildPreviewDocumentAsync_ThemeFlip_ReusesBodyButRebuildsPage()
    {
        var renderer = new CountingRenderer();
        var vm = Create(renderer: renderer);
        vm.ThemeMode = ThemeMode.Light;
        vm.DocumentText = "# theme flip";

        var lightPage = await vm.BuildPreviewDocumentAsync();
        vm.ThemeMode = ThemeMode.Dark;
        var darkPage = await vm.BuildPreviewDocumentAsync();

        Assert.NotSame(lightPage, darkPage);  // theme must re-apply
        Assert.Equal(1, renderer.ParseCalls); // but the markdown must not re-parse
    }

    [Fact]
    public async Task BuildPreviewDocumentAsync_NewText_RendersFreshPage()
    {
        var renderer = new CountingRenderer();
        var vm = Create(renderer: renderer);
        vm.DocumentText = "# one";
        var page1 = await vm.BuildPreviewDocumentAsync();

        vm.DocumentText = "# two";
        var page2 = await vm.BuildPreviewDocumentAsync();

        Assert.NotSame(page1, page2);
        Assert.Equal(2, renderer.ParseCalls);
    }

    [Fact]
    public async Task BuildOutline_AfterPreviewRender_ReusesParse()
    {
        var renderer = new CountingRenderer();
        var vm = Create(renderer: renderer);
        vm.DocumentText = "# heading";

        await vm.BuildPreviewDocumentAsync();
        var outline = vm.BuildOutline();

        Assert.Single(outline);
        Assert.Equal(1, renderer.ParseCalls); // outline shared the preview's parse
    }
}
