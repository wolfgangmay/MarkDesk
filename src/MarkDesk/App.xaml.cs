using System.IO;
using System.Text;
using System.Windows;
using MarkDesk.Services;
using MarkDesk.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace MarkDesk;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        RegisterEncodingProviders();

        Services = ConfigureServices();

        var mainWindow = Services.GetRequiredService<MainWindow>();
        mainWindow.Show();

        if (e.Args.Length > 0 && File.Exists(e.Args[0]))
            mainWindow.ViewModel.OpenPath(e.Args[0]);
    }

    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        services.AddLogging();

        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IEncodingDetector, EncodingDetector>();
        services.AddSingleton<IFileService, FileService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();

        return services.BuildServiceProvider();
    }

    private static void RegisterEncodingProviders()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }
}
