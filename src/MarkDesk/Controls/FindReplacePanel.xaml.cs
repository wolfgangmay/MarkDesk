using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ICSharpCode.AvalonEdit;

namespace MarkDesk.Controls;

public partial class FindReplacePanel : UserControl
{
    private TextEditor? _editor;
    private List<(int Index, int Length)> _matches = new();
    private int _current = -1;

    public FindReplacePanel()
    {
        InitializeComponent();
    }

    public void Attach(TextEditor editor) => _editor = editor;

    public void Show(bool replace)
    {
        Visibility = Visibility.Visible;
        var v = replace ? Visibility.Visible : Visibility.Collapsed;
        ReplaceBox.Visibility = v;
        ReplaceHint.Visibility = v == Visibility.Visible && ReplaceBox.Text.Length == 0
            ? Visibility.Visible : Visibility.Collapsed;
        ReplaceBtn.Visibility = v;
        ReplaceAllBtn.Visibility = v;
        FindBox.Focus();
        FindBox.SelectAll();
    }

    public void Hide() => Visibility = Visibility.Collapsed;

    private StringComparison Comparison =>
        MatchCase.IsChecked == true ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    private void Close_Click(object sender, RoutedEventArgs e) => Hide();

    private void Input_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            if (Keyboard.Modifiers == ModifierKeys.Shift)
                Prev_Click(sender, e);
            else
                Next_Click(sender, e);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Hide();
            _editor?.Focus();
            e.Handled = true;
        }
    }

    private void FindBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        FindHint.Visibility = FindBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        RefreshMatches();
        FindNext();
    }

    private void ReplaceBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        ReplaceHint.Visibility = ReplaceBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Option_Click(object sender, RoutedEventArgs e)
    {
        RefreshMatches();
        FindNext();
    }

    private bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    private bool IsWholeWord(int idx, int len)
    {
        var text = _editor!.Document.Text;
        if (idx > 0 && IsWordChar(text[idx - 1])) return false;
        if (idx + len < text.Length && IsWordChar(text[idx + len])) return false;
        return true;
    }

    private Regex? TryBuildRegex()
    {
        try
        {
            var options = MatchCase.IsChecked == true ? RegexOptions.None : RegexOptions.IgnoreCase;
            return new Regex(FindBox.Text, options);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    // Rebuilds the full match list (index + length) for the current term and
    // options. Regex matches may vary in length, hence the tuple.
    private void RefreshMatches()
    {
        _matches = new List<(int, int)>();
        _current = -1;
        Status.Text = "";

        var term = FindBox.Text;
        if (string.IsNullOrEmpty(term) || _editor == null)
        {
            UpdateCount();
            return;
        }

        var text = _editor.Document.Text;
        if (UseRegex.IsChecked == true)
        {
            var rx = TryBuildRegex();
            if (rx == null)
            {
                Count.Text = "Invalid regex";
                return;
            }
            foreach (Match m in rx.Matches(text))
                if (m.Length > 0)
                    _matches.Add((m.Index, m.Length));
        }
        else
        {
            var whole = WholeWords.IsChecked == true;
            var pos = 0;
            while (term.Length > 0 && pos <= text.Length - term.Length)
            {
                var idx = text.IndexOf(term, pos, Comparison);
                if (idx < 0) break;
                if (!whole || IsWholeWord(idx, term.Length))
                    _matches.Add((idx, term.Length));
                pos = idx + 1;
            }
        }
        UpdateCount();
    }

    private void UpdateCount()
    {
        if (string.IsNullOrEmpty(FindBox.Text)) { Count.Text = ""; return; }
        if (UseRegex.IsChecked == true && TryBuildRegex() == null) return; // keep "Invalid regex"
        Count.Text = _matches.Count == 0
            ? "No results"
            : $"{_current + 1}/{_matches.Count}";
    }

    private void SelectMatch(int i)
    {
        var ed = _editor!;
        var (idx, len) = _matches[i];
        ed.Select(idx, len);
        ed.TextArea.Caret.Offset = idx + len;
        ed.ScrollToLine(ed.Document.GetLineByOffset(idx).LineNumber);
        _current = i;
        UpdateCount();
        Status.Text = "";
    }

    public void FindNext()
    {
        var ed = _editor;
        if (ed == null) return;
        if (string.IsNullOrEmpty(FindBox.Text)) return;
        if (_matches.Count == 0) { Status.Text = "No matches"; return; }

        var from = ed.TextArea.Caret.Offset;
        var i = _matches.FindIndex(m => m.Index >= from);
        SelectMatch(i < 0 ? 0 : i);
    }

    private void Next_Click(object sender, RoutedEventArgs e) => FindNext();

    private void Prev_Click(object sender, RoutedEventArgs e)
    {
        var ed = _editor;
        if (ed == null || _matches.Count == 0 || string.IsNullOrEmpty(FindBox.Text))
        {
            if (!string.IsNullOrEmpty(FindBox.Text)) Status.Text = "No matches";
            return;
        }

        var selectionStart = ed.SelectionStart;
        var i = -1;
        for (var k = 0; k < _matches.Count; k++)
            if (_matches[k].Index < selectionStart)
                i = k;
        SelectMatch(i < 0 ? _matches.Count - 1 : i);
    }

    private bool SelectionIsCurrentMatch =>
        _current >= 0 && _current < _matches.Count &&
        _editor!.SelectionStart == _matches[_current].Index &&
        _editor.SelectionLength == _matches[_current].Length;

    private void Replace_Click(object sender, RoutedEventArgs e)
    {
        var ed = _editor;
        if (ed == null || string.IsNullOrEmpty(FindBox.Text) || _matches.Count == 0) return;
        var repl = ReplaceBox.Text;

        if (!SelectionIsCurrentMatch)
            FindNext();
        if (!SelectionIsCurrentMatch) return;

        var (idx, len) = _matches[_current];
        var newText = repl;
        if (UseRegex.IsChecked == true && TryBuildRegex() is { } rx)
            newText = rx.Replace(ed.SelectedText, repl); // supports $1.. group refs
        ed.Document.Replace(idx, len, newText);
        ed.TextArea.Caret.Offset = idx + newText.Length;
        Status.Text = "Replaced";
        RefreshMatches();
        FindNext();
    }

    private void ReplaceAll_Click(object sender, RoutedEventArgs e)
    {
        var ed = _editor;
        if (ed == null) return;
        var term = FindBox.Text;
        var repl = ReplaceBox.Text;
        if (string.IsNullOrEmpty(term)) return;
        var text = ed.Document.Text;
        int count;

        if (UseRegex.IsChecked == true)
        {
            if (TryBuildRegex() is not { } rx) { Status.Text = "Invalid regex"; return; }
            count = rx.Matches(text).Count;
            ed.Document.Text = rx.Replace(text, repl);
        }
        else
        {
            var whole = WholeWords.IsChecked == true;
            var sb = new StringBuilder(text.Length);
            int pos = 0;
            count = 0;
            while (true)
            {
                if (pos > text.Length - term.Length)
                {
                    sb.Append(text, pos, text.Length - pos);
                    break;
                }
                var idx = text.IndexOf(term, pos, Comparison);
                if (idx < 0)
                {
                    sb.Append(text, pos, text.Length - pos);
                    break;
                }
                sb.Append(text, pos, idx - pos);
                if (whole && !IsWholeWord(idx, term.Length))
                {
                    sb.Append(term);
                    pos = idx + term.Length;
                    continue;
                }
                sb.Append(repl);
                pos = idx + term.Length;
                count++;
            }
            ed.Document.Text = sb.ToString();
        }

        Status.Text = $"{count} replaced";
        RefreshMatches();
    }
}
