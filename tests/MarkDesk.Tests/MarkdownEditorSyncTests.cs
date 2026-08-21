using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using MarkDesk.Controls;

namespace MarkDesk.Tests;

/// <summary>
/// The editor → view-model text push is debounced (150 ms) to avoid a full
/// rope→string build per keystroke. These tests drive the real control and
/// verify all three paths: debounced auto-push, immediate flush, and no
/// stale double-push after a flush.
/// </summary>
[Collection("WpfApp")]
public class MarkdownEditorSyncTests
{
    private static void PumpFrames(int count)
    {
        for (var i = 0; i < count; i++)
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Render);
    }

    [Fact]
    public void EditorText_DebouncedPush_AndFlushPendingText()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var app = PdfNavigationTests.SharedApp;
                var field = typeof(Application).GetField("_resourceAssembly", BindingFlags.Static | BindingFlags.NonPublic);
                field!.SetValue(app, typeof(MarkdownEditor).Assembly);
                var win = new Window { Width = 800, Height = 600 };
                var editor = new MarkdownEditor();
                win.Content = editor;
                win.Show();
                PumpFrames(5);

                // 1) Typing must NOT push synchronously (debounce pending).
                editor.Editor.Document.Insert(0, "hello");
                PumpFrames(10);
                Assert.Equal(string.Empty, editor.DocumentText);

                // 2) Flush delivers the text immediately (save/export path).
                editor.FlushPendingText();
                Assert.Equal("hello", editor.DocumentText);

                // 3) The stopped timer must not push a stale value later.
                Thread.Sleep(300);
                PumpFrames(10);
                Assert.Equal("hello", editor.DocumentText);

                // 4) Without a flush, the debounce timer fires by itself
                //    (Background priority: pump below Render so the tick runs).
                editor.Editor.Document.Insert(0, "world ");
                Thread.Sleep(300);
                for (var i = 0; i < 5; i++)
                {
                    Thread.Sleep(50);
                    Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
                }
                Assert.Equal("world hello", editor.DocumentText);

                win.Close();
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromMinutes(2)))
            throw new Xunit.Sdk.XunitException("test timed out");
        if (failure != null)
            throw new Xunit.Sdk.XunitException(failure.ToString());
    }

    /// <summary>
    /// Crash regression (event log 2026-08-21 20:34): switching from a file
    /// with an active selection to a SHORTER file crashed with
    /// ArgumentOutOfRangeException — AvalonEdit resets the caret inside the
    /// document swap, the CaretPositionChanged subscriber forced a
    /// synchronous layout (outline ScrollIntoView), and SelectionLayer
    /// rendered the old selection offsets against the new document. The
    /// editor must not raise caret events during a document swap.
    /// </summary>
    [Fact]
    public void LoadDocument_WithSelection_SuppressesCaretEvents_NoCrash()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var app = PdfNavigationTests.SharedApp;
                var field = typeof(Application).GetField("_resourceAssembly", BindingFlags.Static | BindingFlags.NonPublic);
                field!.SetValue(app, typeof(MarkdownEditor).Assembly);
                var win = new Window { Width = 800, Height = 600 };
                var editor = new MarkdownEditor();
                win.Content = editor;
                win.Show();
                PumpFrames(5);

                var oldText = new string('x', 600);
                editor.Editor.Document.Text = oldText;
                // 343-char selection exactly as in the crash report.
                editor.Editor.Select(0, 343);
                PumpFrames(5);

                var raisedDuringSwap = false;
                editor.CaretPositionChanged += (_, _) =>
                {
                    raisedDuringSwap = true;
                    // A subscriber forcing layout synchronously is the crash
                    // vector (ScrollIntoView → UpdateLayout).
                    editor.UpdateLayout();
                };

                // Switch to an EMPTY document — the exact fatal combination.
                editor.LoadDocument(new ICSharpCode.AvalonEdit.Document.TextDocument(string.Empty));

                Assert.False(raisedDuringSwap); // guard held: no re-entrant raise
                Assert.Equal(string.Empty, editor.DocumentText);

                // After the swap the guard releases: normal caret events fire.
                var raisedAfter = false;
                editor.CaretPositionChanged += (_, _) => raisedAfter = true;
                editor.Editor.Document.Insert(0, "abc");
                editor.Editor.TextArea.Caret.Offset = 2;
                Assert.True(raisedAfter);

                win.Close();
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromMinutes(2)))
            throw new Xunit.Sdk.XunitException("test timed out");
        if (failure != null)
            throw new Xunit.Sdk.XunitException(failure.ToString());
    }
}
