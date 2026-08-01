using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Xml;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using ICSharpCode.AvalonEdit.Search;

namespace MarkDesk.Controls;

public partial class MarkdownEditor : UserControl
{
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

    public MarkdownEditor()
    {
        InitializeComponent();
        LoadMarkdownHighlighting();
        SearchPanel.Install(Editor);

        Editor.TextChanged += (_, _) => SyncToProperty();
        Editor.TextArea.Caret.PositionChanged += (_, _) => CaretPositionChanged?.Invoke(this, EventArgs.Empty);
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

    public int CaretLine => Editor.TextArea.Caret.Line;
    public int CaretColumn => Editor.TextArea.Caret.Column;

    public event EventHandler? TextChanged;
    public event EventHandler? CaretPositionChanged;

    public void FocusEditor() => Editor.Focus();

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

    private static void OnDocumentTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MarkdownEditor editor && !editor._suppress)
        {
            editor._suppress = true;
            try
            {
                var newText = (e.NewValue as string) ?? string.Empty;
                if (editor.Editor.Document.Text != newText)
                    editor.Editor.Document.Text = newText;
            }
            finally
            {
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
