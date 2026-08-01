using System.IO;

namespace MarkDesk.Services;

public sealed class FileWatcher : IDisposable
{
    private FileSystemWatcher? _watcher;
    private DateTime _ignoreUntil = DateTime.MinValue;

    public event EventHandler? ExternalChanged;

    public void Watch(string? path)
    {
        _watcher?.Dispose();
        _watcher = null;

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

    public void NotifySelfSave() => _ignoreUntil = DateTime.UtcNow.AddSeconds(2);

    private void RaiseIfExternal()
    {
        if (DateTime.UtcNow < _ignoreUntil)
            return;
        ExternalChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose() => _watcher?.Dispose();
}
