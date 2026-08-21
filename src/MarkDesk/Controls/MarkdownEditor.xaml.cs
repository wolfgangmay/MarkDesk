using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using MarkDesk.Services;

namespace MarkDesk.Controls;

public partial class MarkdownEditor : UserControl
{
    public const double MinFontSize = 8;
    public const double MaxFontSize = 36;

    public static readonly DependencyProperty DocumentTextProperty =
        DependencyProperty.Register(
            nameof(DocumentText),
            typeof(string),
            typeof(MarkdownEditor),
            new FrameworkPropertyMetadata(
                string.Empty,
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault | FrameworkPropertyMetadataOptions.Journal,
                OnDocumentTextChanged));

    public static readonly DependencyProperty WordWrapProperty =
        DependencyProperty.Register(
            nameof(WordWrap),
            typeof(bool),
            typeof(MarkdownEditor),
            new FrameworkPropertyMetadata(false, OnWordWrapChanged));

    private bool _suppress;
    private ScrollViewer? _scrollViewer;
    private System.Windows.Threading.DispatcherTimer? _syncTimer;

    /// <summary>
    /// Caret-event re-entrancy guard. Swapping or replacing the document
    /// resets the caret INSIDE the AvalonEdit property change; subscribers
    /// of <see cref="CaretPositionChanged"/> (outline highlight →
    /// ScrollIntoView) then force a synchronous layout over a TextView whose
    /// selection/document state is still mid-swap — e.g. an old selection at
    /// offset 343 resolved against a new empty document crashes with
    /// ArgumentOutOfRangeException in SelectionLayer.OnRender. Caret resets
    /// caused by document swaps are internal, not user actions: don't raise.
    /// </summary>
    private bool _caretEventsSuppressed;

    /// <summary>
    /// Editor → VM pushes are debounced by this interval: AvalonEdit's
    /// Document.Text getter materializes the whole rope, so pushing per
    /// keystroke is O(document) work plus a full-string compare each key.
    /// Consumers that must see the very latest text call
    /// <see cref="FlushPendingText"/> first (save, export, close, reload).
    /// </summary>
    private static readonly TimeSpan SyncDebounce = TimeSpan.FromMilliseconds(150);

    /// <summary>Gate for the typing assists (list continuation, wrapping). Set from settings.</summary>
    public bool TypingAssistsEnabled { get; set; } = true;

    public event EventHandler? ZoomChanged;

    public double EditorFontSize => Editor.FontSize;

    public MarkdownEditor()
    {
        InitializeComponent();
        LoadMarkdownHighlighting();
        Finder.Attach(Editor);

        Editor.TextChanged += (_, _) => SyncToProperty();
        Editor.TextArea.Caret.PositionChanged += (_, _) =>
        {
            if (!_caretEventsSuppressed)
                CaretPositionChanged?.Invoke(this, EventArgs.Empty);
        };
        Editor.PreviewMouseWheel += OnEditorPreviewMouseWheel;
        Editor.TextArea.PreviewKeyDown += OnTextAreaPreviewKeyDown;
        Editor.TextArea.TextEntering += OnTextAreaTextEntering;
        Loaded += OnLoaded;
    }

    private void OnTextAreaTextEntering(object? sender, TextCompositionEventArgs e)
    {
        if (!TypingAssistsEnabled || string.IsNullOrEmpty(e.Text))
            return;
        var c = e.Text[0];

        // Typing the closing char just before an auto-inserted one skips
        // over it instead of doubling it.
        if (Editor.SelectionLength == 0 && c is ']' or ')' or '`' &&
            Editor.CaretOffset < Editor.Document.TextLength &&
            Editor.Document.GetCharAt(Editor.CaretOffset) == c)
        {
            Editor.TextArea.Caret.Offset += 1;
            e.Handled = true;
            return;
        }

        // Wrapping an existing selection: ` * _ ~ [ (
        if (Editor.SelectionLength > 0 && c is '`' or '*' or '_' or '~' or '[' or '(')
        {
            var open = c is '[' or '(' ? c.ToString() : e.Text;
            var close = c switch { '[' => "]", '(' => ")", _ => e.Text };
            var selected = Editor.SelectedText;
            var start = Editor.SelectionStart;
            Editor.Document.Replace(start, Editor.SelectionLength, open + selected + close);
            Editor.Select(start + open.Length, selected.Length);
            e.Handled = true;
            return;
        }

        // Bare pair completion for ` [ (
        if (Editor.SelectionLength == 0 && MarkdownWrapping.IsPairedChar(c))
        {
            var offset = Editor.CaretOffset;
            Editor.Document.Insert(offset, e.Text + MarkdownWrapping.ClosingFor(c));
            Editor.TextArea.Caret.Offset = offset + 1;
            e.Handled = true;
        }
    }

    private void ToggleWrap(string token)
    {
        if (Editor.SelectionLength > 0)
        {
            var start = Editor.SelectionStart;
            var replaced = MarkdownWrapping.Toggle(Editor.SelectedText, token);
            Editor.Document.Replace(start, Editor.SelectionLength, replaced);
            Editor.Select(start, replaced.Length);
        }
        else
        {
            var offset = Editor.CaretOffset;
            Editor.Document.Insert(offset, token + token);
            Editor.TextArea.Caret.Offset = offset + token.Length;
        }
    }

    private void ToggleLink()
    {
        if (Editor.SelectionLength > 0)
        {
            var start = Editor.SelectionStart;
            var replaced = MarkdownWrapping.ToggleLink(Editor.SelectedText);
            if (replaced == null)
                return;
            Editor.Document.Replace(start, Editor.SelectionLength, replaced);
            if (replaced.EndsWith("()", StringComparison.Ordinal))
                Editor.TextArea.Caret.Offset = start + replaced.Length - 1; // type the URL
            else
                Editor.Select(start, replaced.Length);
        }
        else
        {
            var offset = Editor.CaretOffset;
            Editor.Document.Insert(offset, "[]()");
            Editor.TextArea.Caret.Offset = offset + 1;
        }
    }

    private void OnTextAreaPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (TypingAssistsEnabled && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (e.Key == Key.B)
            {
                ToggleWrap("**");
                e.Handled = true;
                return;
            }
            if (e.Key == Key.I)
            {
                ToggleWrap("*");
                e.Handled = true;
                return;
            }
            if (e.Key == Key.K)
            {
                ToggleLink();
                e.Handled = true;
                return;
            }
        }

        if (e.Key != Key.Enter || Keyboard.Modifiers != ModifierKeys.None)
            return;
        if (!TypingAssistsEnabled)
            return;

        var doc = Editor.Document;
        var line = doc.GetLineByOffset(Editor.CaretOffset);
        if (IsInsideFenceBeforeLine(line))
            return;

        var text = doc.GetText(line.Offset, line.Length);
        var item = ListContinuation.ParseItem(text);
        if (item == null)
            return;

        if (ListContinuation.IsEmpty(item) && Editor.CaretOffset >= line.Offset + line.Length)
        {
            // Empty item: Enter exits the list — clear the marker line entirely.
            doc.Replace(line.Offset, line.Length, string.Empty);
            Editor.TextArea.Caret.Offset = line.Offset;
        }
        else
        {
            // Insert the continuation prefix at the caret; text after the
            // caret (mid-item Enter) flows onto the new line as its content.
            doc.Insert(Editor.CaretOffset, "\n" + ListContinuation.BuildNextLinePrefix(item));
        }
        e.Handled = true;
    }

    private bool IsInsideFenceBeforeLine(ICSharpCode.AvalonEdit.Document.DocumentLine current)
    {
        IEnumerable<string> LinesBefore()
        {
            foreach (var l in Editor.Document.Lines)
            {
                if (ReferenceEquals(l, current))
                    yield break;
                yield return Editor.Document.GetText(l.Offset, l.Length);
            }
        }
        return ListContinuation.IsInsideFence(LinesBefore());
    }

    public void SetFontSize(double size) =>
        Editor.FontSize = Math.Clamp(size, MinFontSize, MaxFontSize);

    private void OnEditorPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0)
            return;
        e.Handled = true;
        var next = Math.Clamp(Editor.FontSize + (e.Delta > 0 ? 1 : -1), MinFontSize, MaxFontSize);
        if (Math.Abs(next - Editor.FontSize) < 0.01)
            return;
        Editor.FontSize = next;
        ZoomChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ShowSearch() { Editor.Focus(); Finder.Show(false); }
    public void ShowReplace() { Editor.Focus(); Finder.Show(true); }

    public void ApplyTheme(bool dark)
    {
        var area = Editor.TextArea;
        if (dark)
        {
            var bg = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x1E));
            var fg = new SolidColorBrush(Color.FromRgb(0xD4, 0xD4, 0xD4));
            Editor.Background = bg;
            area.Background = bg;
            area.Foreground = fg;
        }
        else
        {
            Editor.Background = Brushes.White;
            area.Background = Brushes.White;
            area.Foreground = new SolidColorBrush(Color.FromRgb(0x1F, 0x1F, 0x1F));
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _scrollViewer = FindVisualChild<ScrollViewer>(Editor);
        if (_scrollViewer != null)
            _scrollViewer.ScrollChanged += (_, _) => ScrollChanged?.Invoke(this, EventArgs.Empty);
    }

    public double ScrollProportion
    {
        get
        {
            var sv = _scrollViewer;
            return sv is null || sv.ScrollableHeight <= 0 ? 0 : sv.VerticalOffset / sv.ScrollableHeight;
        }
        set
        {
            var sv = _scrollViewer;
            if (sv is not null && sv.ScrollableHeight > 0)
                sv.ScrollToVerticalOffset(value * sv.ScrollableHeight);
        }
    }

    public event EventHandler? ScrollChanged;

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T matched)
                return matched;
            var result = FindVisualChild<T>(child);
            if (result != null)
                return result;
        }
        return null;
    }

    public string DocumentText
    {
        get => (string)GetValue(DocumentTextProperty);
        set => SetValue(DocumentTextProperty, value);
    }

    public bool WordWrap
    {
        get => (bool)GetValue(WordWrapProperty);
        set => SetValue(WordWrapProperty, value);
    }

    /// <summary>Large-file mode: browsing only, no edits.</summary>
    public bool IsReadOnly
    {
        get => Editor.IsReadOnly;
        set => Editor.IsReadOnly = value;
    }

    public int CaretLine => Editor.TextArea.Caret.Line;
    public int CaretColumn => Editor.TextArea.Caret.Column;

    public event EventHandler? TextChanged;
    public event EventHandler? CaretPositionChanged;

    public void FocusEditor() => Editor.Focus();

    public void ScrollToLine(int line) => Editor.ScrollToLine(line);

    public void InsertAtCaret(string text)
    {
        var offset = Editor.TextArea.Caret.Offset;
        Editor.Document.Insert(offset, text);
        Editor.TextArea.Caret.Offset = offset + text.Length;
        Editor.Focus();
    }

    public void ReplaceAll(string text)
    {
        _suppress = true;
        try
        {
            Editor.Document.Text = text;
        }
        finally
        {
            _suppress = false;
        }
    }

    /// <summary>
    /// Loads a pre-built document (large-file mode): the rope structure is
    /// built off the UI thread, so this swaps only a reference — far cheaper
    /// than assigning Document.Text on the UI thread. Pass the view-model's
    /// text instance when available: the dependency property and the view
    /// model then share one string reference, and every later equality check
    /// (binding push-back, VM SetProperty) short-circuits instead of doing
    /// an O(n) build+compare of the same multi-MB content.
    /// </summary>
    public void LoadDocument(ICSharpCode.AvalonEdit.Document.TextDocument document, string? text = null)
    {
        _caretEventsSuppressed = true;
        _suppress = true;
        try
        {
            Editor.Document = document;
            SetCurrentValue(DocumentTextProperty, text ?? document.Text);
        }
        finally
        {
            _suppress = false;
            _caretEventsSuppressed = false;
        }
    }

    /// <summary>Syntax highlighting off for large-file mode (3+ MB documents).</summary>
    public void SetHighlighting(bool enabled)
    {
        if (enabled)
        {
            if (Editor.SyntaxHighlighting == null)
                LoadMarkdownHighlighting();
        }
        else
        {
            Editor.SyntaxHighlighting = null;
        }
    }

    private static void OnDocumentTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MarkdownEditor editor && !editor._suppress)
        {
            editor._suppress = true;
            editor._caretEventsSuppressed = true;
            try
            {
                var newText = (e.NewValue as string) ?? string.Empty;
                if (editor.Editor.Document.Text != newText)
                    editor.Editor.Document.Text = newText;
            }
            finally
            {
                editor._caretEventsSuppressed = false;
                editor._suppress = false;
            }
        }
    }

    private static void OnWordWrapChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MarkdownEditor editor)
            editor.Editor.WordWrap = (bool)e.NewValue;
    }

    private void SyncToProperty()
    {
        TextChanged?.Invoke(this, EventArgs.Empty);
        if (_suppress)
            return;
        if (_syncTimer == null)
        {
            _syncTimer = new System.Windows.Threading.DispatcherTimer(
                System.Windows.Threading.DispatcherPriority.Background)
            {
                Interval = SyncDebounce
            };
            _syncTimer.Tick += (_, _) => FlushPendingText();
        }
        _syncTimer.Stop();
        _syncTimer.Start();
    }

    /// <summary>
    /// Pushes the editor text to the DocumentText property immediately if a
    /// debounced push is still pending. Call before reading the bound
    /// view-model text (save, PDF export, close, external reload).
    /// </summary>
    public void FlushPendingText()
    {
        _syncTimer?.Stop();
        if (_suppress)
            return;
        _suppress = true;
        try
        {
            var current = Editor.Document.Text;
            if (DocumentText != current)
                SetCurrentValue(DocumentTextProperty, current);
        }
        finally
        {
            _suppress = false;
        }
    }

    private void LoadMarkdownHighlighting()
    {
        var uri = new Uri("pack://application:,,,/Assets/Markdown.xshd");
        var resource = Application.GetResourceStream(uri);
        if (resource == null)
            return;
        using var stream = resource.Stream;
        using var reader = XmlReader.Create(stream);
        Editor.SyntaxHighlighting = HighlightingLoader.Load(reader, HighlightingManager.Instance);
    }
}
