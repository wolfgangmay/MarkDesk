using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MarkDesk.Services;

namespace MarkDesk.Controls;

public partial class ThemedMessageBox : Window
{
    private MessageBoxResult _result = MessageBoxResult.None;

    private ThemedMessageBox(string message, string title, MessageBoxButton buttons, MessageBoxImage image)
    {
        InitializeComponent();
        Title = title;
        MessageText.Text = message;

        var (glyph, color) = IconGlyph(image);
        IconHost.Text = glyph;
        IconHost.Foreground = new SolidColorBrush(color);

        AddButtons(buttons);
        Loaded += (_, _) => WindowTheme.ApplyTitleBar(this);
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                _result = EscResult(buttons);
                DialogResult = true;
            }
        };
    }

    public static MessageBoxResult Show(Window? owner, string message, string title,
        MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage image = MessageBoxImage.None)
    {
        var box = new ThemedMessageBox(message, title, buttons, image);
        if (owner != null && owner.IsVisible)
            box.Owner = owner;
        box.ShowDialog();
        return box._result == MessageBoxResult.None ? EscResult(buttons) : box._result;
    }

    private void AddButtons(MessageBoxButton buttons)
    {
        void Add(string label, MessageBoxResult r, bool accent, bool isDefault, bool isCancel)
        {
            var btn = new Button
            {
                Content = label,
                Style = (Style)Application.Current.FindResource(accent ? "ThemedAccentButton" : "ThemedButton"),
                MinWidth = 84,
                Margin = new Thickness(6, 0, 0, 0),
                IsDefault = isDefault,
                IsCancel = isCancel
            };
            btn.Click += (_, _) => { _result = r; DialogResult = true; };
            ButtonPanel.Children.Add(btn);
        }

        switch (buttons)
        {
            case MessageBoxButton.OK:
                Add("OK", MessageBoxResult.OK, true, true, true);
                break;
            case MessageBoxButton.YesNo:
                Add("Yes", MessageBoxResult.Yes, true, true, false);
                Add("No", MessageBoxResult.No, false, false, true);
                break;
            case MessageBoxButton.YesNoCancel:
                Add("Yes", MessageBoxResult.Yes, true, true, false);
                Add("No", MessageBoxResult.No, false, false, false);
                Add("Cancel", MessageBoxResult.Cancel, false, false, true);
                break;
            case MessageBoxButton.OKCancel:
                Add("OK", MessageBoxResult.OK, true, true, false);
                Add("Cancel", MessageBoxResult.Cancel, false, false, true);
                break;
        }
    }

    private static MessageBoxResult EscResult(MessageBoxButton buttons) => buttons switch
    {
        MessageBoxButton.YesNo => MessageBoxResult.No,
        MessageBoxButton.YesNoCancel or MessageBoxButton.OKCancel => MessageBoxResult.Cancel,
        _ => MessageBoxResult.None
    };

    private static (string Glyph, Color Color) IconGlyph(MessageBoxImage image) => image switch
    {
        MessageBoxImage.Warning => ("\xE7BA", Color.FromRgb(0x9A, 0x67, 0x00)),
        MessageBoxImage.Error => ("\xE783", Color.FromRgb(0xCF, 0x22, 0x2E)),
        MessageBoxImage.Information => ("\xE946", Color.FromRgb(0x09, 0x69, 0xDA)),
        MessageBoxImage.Question => ("\xE897", Color.FromRgb(0x09, 0x69, 0xDA)),
        _ => ("", Color.FromRgb(0x09, 0x69, 0xDA))
    };
}
