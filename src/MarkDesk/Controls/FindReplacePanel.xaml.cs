using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ICSharpCode.AvalonEdit;

namespace MarkDesk.Controls;

public partial class FindReplacePanel : UserControl
{
    private TextEditor? _editor;
    private int _lastFound = -1;

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
        _lastFound = -1;
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

    private int SearchForward(int from)
    {
        var ed = _editor;
        if (ed == null) return -1;
        var term = FindBox.Text;
        if (string.IsNullOrEmpty(term)) return -1;
        var text = ed.Document.Text;
        var whole = WholeWords.IsChecked == true;
        var start = from;
        while (true)
        {
            var idx = start > text.Length
                ? -1
                : text.IndexOf(term, start, Comparison);
            if (idx < 0 && start > 0) // wrap
            {
                start = 0;
                continue;
            }
            if (idx < 0) return -1;
            if (whole && !IsWholeWord(idx, term.Length))
            {
                start = idx + 1;
                continue;
            }
            return idx;
        }
    }

    private void Select(int idx, int len)
    {
        var ed = _editor!;
        ed.Select(idx, len);
        ed.TextArea.Caret.Offset = idx + len;
        ed.ScrollToLine(ed.Document.GetLineByOffset(idx).LineNumber);
        _lastFound = idx;
    }

    public void FindNext()
    {
        var ed = _editor;
        if (ed == null) return;
        var term = FindBox.Text;
        if (string.IsNullOrEmpty(term)) { Status.Text = ""; return; }
        var from = ed.TextArea.Caret.Offset;
        if (ed.SelectionStart == _lastFound && ed.SelectionLength == term.Length)
            from = _lastFound + term.Length;
        var idx = SearchForward(from);
        if (idx >= 0) { Select(idx, term.Length); Status.Text = ""; }
        else Status.Text = "No matches";
    }

    private void Next_Click(object sender, RoutedEventArgs e) => FindNext();

    private void Prev_Click(object sender, RoutedEventArgs e)
    {
        var ed = _editor;
        if (ed == null) return;
        var term = FindBox.Text;
        if (string.IsNullOrEmpty(term)) return;
        var text = ed.Document.Text;
        var whole = WholeWords.IsChecked == true;
        var end = ed.SelectionStart;
        int last = -1, s = 0;
        while (s <= end && s < text.Length)
        {
            var f = text.IndexOf(term, s, end - s, Comparison);
            if (f < 0) break;
            if (!whole || IsWholeWord(f, term.Length)) last = f;
            s = f + 1;
        }
        if (last >= 0) { Select(last, term.Length); Status.Text = ""; }
        else Status.Text = "No matches";
    }

    private void Replace_Click(object sender, RoutedEventArgs e)
    {
        var ed = _editor;
        if (ed == null) return;
        var term = FindBox.Text;
        var repl = ReplaceBox.Text;
        if (string.IsNullOrEmpty(term)) return;
        if (ed.SelectionLength == term.Length &&
            string.Equals(ed.SelectedText, term, Comparison))
        {
            ed.Document.Replace(ed.SelectionStart, ed.SelectionLength, repl);
        }
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
        var whole = WholeWords.IsChecked == true;
        var sb = new StringBuilder(text.Length);
        int pos = 0, count = 0;
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
        Status.Text = $"{count} replaced";
    }
}
