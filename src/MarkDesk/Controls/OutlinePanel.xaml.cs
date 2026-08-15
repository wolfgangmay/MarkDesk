using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace MarkDesk.Controls;

public partial class OutlinePanel : UserControl
{
    /// <summary>Row view-model: indent by level, highlight the current section.</summary>
    public sealed partial class Row : ObservableObject
    {
        public required int Level { get; init; }
        public required int Line { get; init; }
        public required string Text { get; init; }

        [ObservableProperty]
        private bool _isCurrent;

        public Thickness Indent => new((Level - 1) * 14, 0, 0, 0);
    }

    private List<Row> _rows = new();
    private int _currentLine = -1;

    /// <summary>Raised with the 1-based editor line of the clicked heading.</summary>
    public event Action<int>? HeadingClicked;

    public OutlinePanel()
    {
        InitializeComponent();
    }

    public void SetHeadings(IReadOnlyList<Services.OutlineItem> items)
    {
        _rows = items.Select(i => new Row { Level = i.Level, Line = i.Line, Text = i.Text }).ToList();
        EmptyHint.Text = "No headings in this document";
        EmptyHint.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        List.ItemsSource = _rows;
        Refresh(highlight: true);
    }

    /// <summary>Background outline parse in progress (medium/large tiers).</summary>
    public void SetLoading(bool loading)
    {
        if (loading)
        {
            EmptyHint.Text = "Parsing…";
            EmptyHint.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    /// <summary>Highlights the heading containing the given editor line.</summary>
    public void HighlightLine(int editorLine)
    {
        if (editorLine == _currentLine)
            return;
        _currentLine = editorLine;
        Refresh(highlight: true);
    }

    private void Refresh(bool highlight)
    {
        if (!highlight)
        {
            List.ItemsSource = _rows;
            return;
        }

        // Find the deepest heading above the caret. Only the previous and new
        // current rows change (INPC), so this is O(rows) work but O(1) UI
        // updates — never a full ItemsSource rebuild (thousands of rows would
        // freeze the UI for seconds).
        var currentIdx = -1;
        if (_currentLine >= 0)
            for (var i = 0; i < _rows.Count; i++)
                if (_rows[i].Line <= _currentLine)
                    currentIdx = i;
        for (var i = 0; i < _rows.Count; i++)
            _rows[i].IsCurrent = i == currentIdx;

        if (currentIdx < 0)
            return;
        if (List.ItemContainerGenerator.ContainerFromIndex(currentIdx) is FrameworkElement container)
            container.BringIntoView();
        else
            List.ScrollIntoView(_rows[currentIdx]);
    }

    private void Row_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Row row })
            HeadingClicked?.Invoke(row.Line);
    }
}
