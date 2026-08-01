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
    }

    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        services.AddLogging();

        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();

        return services.BuildServiceProvider();
    }

    private static void RegisterEncodingProviders()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }
}
