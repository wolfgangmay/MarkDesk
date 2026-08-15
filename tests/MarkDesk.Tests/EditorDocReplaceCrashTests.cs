using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Utils;
using MarkDesk.Controls;

namespace MarkDesk.Tests;

[Collection("WpfApp")]
public class EditorDocReplaceCrashTests
{
    private static string BuildHugeText(int lines)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < lines; i++)
            sb.AppendLine("## section " + i);
        return sb.ToString();
    }

    [Fact]
    public void RopeTextDocument_ConstructionTime_Large()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var rope = new Rope<char>(BuildHugeText(400_000));
                var sw = Stopwatch.StartNew();
                var doc = new TextDocument(new RopeTextSource(rope));
                sw.Stop();
                Console.WriteLine($"TextDocument(rope) 400k lines took {sw.ElapsedMilliseconds} ms, LineCount={doc.LineCount}");
                Assert.True(doc.LineCount == 400_001);
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null)
            throw new Xunit.Sdk.XunitException(failure.ToString());
    }

    private static void PumpFrames(int count)
    {
        for (var i = 0; i < count; i++)
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Render);
    }

    [Fact]
    public void FullWindow_Load6MB_ThenSmall_NoCrash()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var app = PdfNavigationTests.SharedApp;
                var field = typeof(Application).GetField("_resourceAssembly", BindingFlags.Static | BindingFlags.NonPublic);
                field!.SetValue(app, typeof(MarkdownEditor).Assembly);
                var win = new Window { Width = 1200, Height = 800 };
                var editor = new MarkdownEditor();
                win.Content = editor;
                win.Show();
                PumpFrames(5);

                editor.LoadDocument(new TextDocument(BuildHugeText(400_000)));
                editor.SetHighlighting(false);
                PumpFrames(10);
                Console.WriteLine("phase1 (6MB loaded) ok");

                editor.LoadDocument(new TextDocument("# back to small"));
                editor.SetHighlighting(true);
                PumpFrames(10);
                Console.WriteLine("phase2 (small) ok");

                win.Close();
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromMinutes(3)))
            throw new Xunit.Sdk.XunitException("FullWindow test timed out after 3 minutes");
        if (failure != null)
            throw new Xunit.Sdk.XunitException(failure.ToString());
    }
}