using System.Windows;
using System.Windows.Input;
using MarkDesk.ViewModels;

namespace MarkDesk;

public partial class MainWindow : Window
{
    public static readonly RoutedUICommand CycleViewModeCommand = new(
        "Cycle View Mode", "CycleViewMode", typeof(MainWindow));

    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;
    }

    private MainViewModel ViewModel => _viewModel;

    private void Open_Executed(object sender, ExecutedRoutedEventArgs e)
    {
    }

    private void Save_Executed(object sender, ExecutedRoutedEventArgs e)
    {
    }

    private void SaveAs_Executed(object sender, ExecutedRoutedEventArgs e)
    {
    }

    private void ExportPdf_Executed(object sender, ExecutedRoutedEventArgs e)
    {
    }

    private void Exit_Executed(object sender, ExecutedRoutedEventArgs e) => Close();

    private void CycleViewMode_Executed(object sender, ExecutedRoutedEventArgs e)
        => ViewModel.CycleViewModeCommand.Execute(null);
}
