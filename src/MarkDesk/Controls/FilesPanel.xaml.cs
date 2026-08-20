using System.IO;
using System.Windows;
using System.Windows.Controls;
using MarkDesk.Services;

namespace MarkDesk.Controls;

/// <summary>
/// Left-side workspace browser. Shows the markdown files of one directory
/// level ("root"); sub-directories expand lazily in the background (never
/// blocking the UI on slow disks), the header up-button re-roots to the
/// parent. Clicking a file raises <see cref="OpenFileRequested"/>; clicking a
/// directory row re-roots into it.
/// </summary>
public partial class FilesPanel : UserControl
{
    /// <summary>Raised when the user clicks a markdown file (full path).</summary>
    public event Action<string>? OpenFileRequested;

    /// <summary>Raised when the user navigated the tree to a new root.</summary>
    public event Action<string>? RootChanged;

    private string? _root;
    private string? _currentFile;
    private bool _building;
    private FileSystemWatcher? _dirWatcher;
    private readonly System.Windows.Threading.DispatcherTimer _refreshDebounce;

    public FilesPanel()
    {
        InitializeComponent();
        _refreshDebounce = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _refreshDebounce.Tick += (_, _) => { _refreshDebounce.Stop(); Rebuild(); };
        Unloaded += (_, _) => _dirWatcher?.Dispose();
    }

    /// <summary>Re-roots the tree and rebuilds the top level in the background.</summary>
    public void SetRoot(string? root, string? currentFile)
    {
        _currentFile = currentFile;
        // Opening another file in the same folder only moves the highlight;
        // the tree (and its expansion state) is not rebuilt.
        if (!string.IsNullOrEmpty(_root) && string.Equals(root, _root, StringComparison.OrdinalIgnoreCase))
        {
            SetCurrentFile(currentFile);
            return;
        }

        _root = root;
        RootPath.Text = root ?? string.Empty;
        UpButton.IsEnabled = root != null && WorkspaceScanner.ParentOf(root) != null;
        WatchRoot(root);
        Rebuild();
    }

    /// <summary>Updates the current-file highlight without re-rooting.</summary>
    public void SetCurrentFile(string? currentFile)
    {
        _currentFile = currentFile;
        foreach (var item in Tree.Items.OfType<TreeViewItem>())
            ApplySelection(item);
    }

    /// <summary>Re-enumerates the root level (e.g. after external changes).</summary>
    public void Refresh() => Rebuild();

    private void Rebuild()
    {
        if (_root == null || _building)
        {
            if (_root == null)
                Tree.Items.Clear();
            return;
        }

        _building = true;
        var root = _root;
        var version = Guid.NewGuid();
        Task.Run(() => WorkspaceScanner.ListEntries(root)).ContinueWith(t =>
        {
            Dispatcher.Invoke(() =>
            {
                _building = false;
                if (t.IsFaulted || root != _root)
                    return;
                Tree.Items.Clear();
                foreach (var entry in t.Result)
                    Tree.Items.Add(CreateNode(entry));
            });
        }, TaskScheduler.Default);
    }

    private TreeViewItem CreateNode(WorkspaceEntry entry)
    {
        var item = new TreeViewItem
        {
            Header = entry.Name,
            Tag = entry
        };
        // Placeholder keeps the expand arrow visible until real children load.
        if (entry.IsDirectory)
            item.Items.Add(new TreeViewItem());
        ApplySelection(item);
        return item;
    }

    private void OnItemExpanded(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not TreeViewItem item || item.Tag is not WorkspaceEntry entry || !entry.IsDirectory)
            return;

        // Strip the placeholder and load the real children once, in the
        // background; the tree stays responsive even on network shares.
        if (item.Items.Count == 1 && item.Items[0] is TreeViewItem { Tag: null })
            item.Items.Clear();
        else if (item.Items.Count > 0)
            return; // already loaded

        item.Items.Add(new TreeViewItem { Tag = LoadingMarker });
        var path = entry.FullPath;
        Task.Run(() => WorkspaceScanner.ListEntries(path)).ContinueWith(t =>
        {
            Dispatcher.Invoke(() =>
            {
                if (t.IsFaulted)
                {
                    RemoveLoadingMarker(item);
                    return;
                }
                RemoveLoadingMarker(item);
                if (item.Items.Count > 0)
                    return; // a concurrent load already populated it
                foreach (var child in t.Result)
                    item.Items.Add(CreateNode(child));
            });
        }, TaskScheduler.Default);
    }

    private static readonly object LoadingMarker = new();

    private static void RemoveLoadingMarker(TreeViewItem item)
    {
        if (item.Items.Count == 1 && ReferenceEquals((item.Items[0] as TreeViewItem)?.Tag, LoadingMarker))
            item.Items.Clear();
    }

    private void OnItemSelected(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not TreeViewItem item || item.Tag is not WorkspaceEntry entry)
            return;
        e.Handled = true;

        if (!entry.IsDirectory)
        {
            OpenFileRequested?.Invoke(entry.FullPath);
            return;
        }

        // Clicking a directory row re-roots the tree into it ("navigate down").
        SetRoot(entry.FullPath, _currentFile);
        RootChanged?.Invoke(entry.FullPath);
    }

    private void ApplySelection(TreeViewItem item)
    {
        if (item.Tag is WorkspaceEntry entry && !string.IsNullOrEmpty(_currentFile) &&
            string.Equals(entry.FullPath, _currentFile, StringComparison.OrdinalIgnoreCase))
            item.IsSelected = true;
    }

    private void UpButton_Click(object sender, RoutedEventArgs e)
    {
        var parent = _root != null ? WorkspaceScanner.ParentOf(_root) : null;
        if (parent == null)
            return;
        SetRoot(parent, _currentFile);
        RootChanged?.Invoke(parent);
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e) => Refresh();

    /// <summary>
    /// Watches the root folder for files/folders appearing, disappearing or
    /// being renamed so the tree stays fresh (e.g. files created by other
    /// tools). Plain content writes to existing files are deliberately not
    /// observed — saving the open document would otherwise rebuild the tree
    /// and discard the expansion state for no visible change.
    /// </summary>
    private void WatchRoot(string? root)
    {
        _dirWatcher?.Dispose();
        _dirWatcher = null;
        if (root == null)
            return;
        try
        {
            _dirWatcher = new FileSystemWatcher(root)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName,
                EnableRaisingEvents = true
            };
            _dirWatcher.Created += (_, _) => ScheduleRefresh();
            _dirWatcher.Deleted += (_, _) => ScheduleRefresh();
            _dirWatcher.Renamed += (_, _) => ScheduleRefresh();
        }
        catch
        {
            // Watching optional; ignore failures (e.g. network paths).
        }
    }

    private void ScheduleRefresh() => Dispatcher.BeginInvoke(() =>
    {
        _refreshDebounce.Stop();
        _refreshDebounce.Start();
    });
}
