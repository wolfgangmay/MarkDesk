using System.Windows;
using MarkDesk.ViewModels;

namespace MarkDesk.Views;

public partial class SettingsDialog : Window
{
    private readonly SettingsViewModel _viewModel;

    public SettingsDialog(SettingsViewModel viewModel, int currentWindowWidth)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _viewModel.CurrentWindowWidth = currentWindowWidth;
        _viewModel.Load();
        DataContext = _viewModel;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.Save();
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
