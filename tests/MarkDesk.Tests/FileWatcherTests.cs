using System.IO;
using MarkDesk.Services;

namespace MarkDesk.Tests;

public class FileWatcherTests
{
    private static string NewTempDir() =>
        Path.Combine(Path.GetTempPath(), "MarkDeskTest_" + Guid.NewGuid().ToString("N"));

    private static List<DateTime> StartWatching(FileWatcher watcher, string path)
    {
        var events = new List<DateTime>();
        watcher.ExternalChanged += (_, _) =>
        {
            lock (events)
                events.Add(DateTime.UtcNow);
        };
        watcher.Watch(path);
        return events;
    }

    [Fact]
    public async Task Merges_BurstOfExternalChanges_IntoSingleEvent()
    {
        var dir = NewTempDir();
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "doc.md");
            File.WriteAllText(path, "v1");

            using var watcher = new FileWatcher();
            var events = StartWatching(watcher, path);

            File.WriteAllText(path, "v2");
            File.WriteAllText(path, "v3");
            File.WriteAllText(path, "v4");

            await Task.Delay(1200);

            lock (events)
                Assert.Single(events);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task NotifySelfSave_SuppressesExternalEvents()
    {
        var dir = NewTempDir();
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "doc.md");
            File.WriteAllText(path, "v1");

            using var watcher = new FileWatcher();
            var events = StartWatching(watcher, path);

            watcher.NotifySelfSave();
            File.WriteAllText(path, "v2");

            await Task.Delay(1000);

            lock (events)
                Assert.Empty(events);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }

    [Fact]
    public async Task Raises_Again_AfterQuietPeriod()
    {
        var dir = NewTempDir();
        Directory.CreateDirectory(dir);
        try
        {
            var path = Path.Combine(dir, "doc.md");
            File.WriteAllText(path, "v1");

            using var watcher = new FileWatcher();
            var events = StartWatching(watcher, path);

            File.WriteAllText(path, "v2");
            await Task.Delay(1200);

            File.WriteAllText(path, "v3");
            await Task.Delay(1200);

            lock (events)
                Assert.Equal(2, events.Count);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
    }
}
