using System.IO;

namespace MarkDesk.Services;

public sealed class FileWatcher : IDisposable
{
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(300);

    private FileSystemWatcher? _watcher;
    private readonly object _gate = new();
    private readonly Timer _debounceTimer;
    private DateTime _ignoreUntil = DateTime.MinValue;

    public FileWatcher()
    {
        _debounceTimer = new Timer(_ => DebounceElapsed());
    }

    public event EventHandler? ExternalChanged;

    public void Watch(string? path)
    {
        _watcher?.Dispose();
        _watcher = null;
        StopDebounce();

        if (string.IsNullOrEmpty(path))
            return;

        var dir = Path.GetDirectoryName(path);
        var name = Path.GetFileName(path);
        if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(name))
            return;

        try
        {
            _watcher = new FileSystemWatcher(dir, name)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                EnableRaisingEvents = true
            };
            _watcher.Changed += (_, _) => RaiseIfExternal();
            _watcher.Created += (_, _) => RaiseIfExternal();
            _watcher.Deleted += (_, _) => RaiseIfExternal();
            _watcher.Renamed += (_, _) => RaiseIfExternal();
        }
        catch
        {
            // Watching optional; ignore failures (e.g. network paths).
        }
    }

    public void NotifySelfSave()
    {
        lock (_gate)
        {
            _ignoreUntil = DateTime.UtcNow.AddSeconds(2);
            _debounceTimer.Change(Timeout.Infinite, Timeout.Infinite);
        }
    }

    private void RaiseIfExternal()
    {
        lock (_gate)
        {
            if (DateTime.UtcNow < _ignoreUntil)
                return;
            _debounceTimer.Change(DebounceDelay, Timeout.InfiniteTimeSpan);
        }
    }

    private void DebounceElapsed()
    {
        EventHandler? handler;
        lock (_gate)
        {
            if (DateTime.UtcNow < _ignoreUntil)
                return;
            handler = ExternalChanged;
        }
        handler?.Invoke(this, EventArgs.Empty);
    }

    private void StopDebounce()
    {
        lock (_gate)
            _debounceTimer.Change(Timeout.Infinite, Timeout.Infinite);
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        _debounceTimer.Dispose();
    }
}
