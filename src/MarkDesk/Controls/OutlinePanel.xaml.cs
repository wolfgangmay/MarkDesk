using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MarkDesk.Controls;

public partial class OutlinePanel : UserControl
{
    /// <summary>Row view-model: indent by level, highlight the current section.</summary>
    public sealed class Row
    {
        public required int Level { get; init; }
        public required int Line { get; init; }
        public required string Text { get; init; }
        public bool IsCurrent { get; set; }
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
        EmptyHint.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        Refresh(highlight: true);
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
        if (highlight)
        {
            var currentIdx = -1;
            if (_currentLine >= 0)
                for (var i = 0; i < _rows.Count; i++)
                    if (_rows[i].Line <= _currentLine)
                        currentIdx = i;
            for (var i = 0; i < _rows.Count; i++)
                _rows[i].IsCurrent = i == currentIdx;
        }

        List.ItemsSource = null;
        List.ItemsSource = _rows;

        // Scroll the current row into view (index-based, cheap).
        if (highlight)
        {
            var idx = _rows.FindIndex(r => r.IsCurrent);
            if (idx >= 0 && List.ItemContainerGenerator.ContainerFromIndex(idx) is FrameworkElement container)
                container.BringIntoView();
        }
    }

    private void Row_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Row row })
            HeadingClicked?.Invoke(row.Line);
    }
}
