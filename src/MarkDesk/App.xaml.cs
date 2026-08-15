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

    private static readonly string CrashLogPath =
        Path.Combine(Path.GetTempPath(), "MarkDesk-crash.log");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        RegisterEncodingProviders();

        DispatcherUnhandledException += (_, args) =>
        {
            WriteCrashLog("DispatcherUnhandledException", args.Exception);
            args.Handled = false;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            WriteCrashLog("AppDomain.UnhandledException", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
            WriteCrashLog("UnobservedTaskException", args.Exception);

        Services = ConfigureServices();

        var mainWindow = Services.GetRequiredService<MainWindow>();
        mainWindow.Show();

        if (e.Args.Length > 0 && File.Exists(e.Args[0]))
            mainWindow.ViewModel.OpenPath(e.Args[0]);
    }

    private static void WriteCrashLog(string source, Exception? ex)
    {
        if (ex == null)
            return;
        try
        {
            File.AppendAllText(CrashLogPath,
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}\n{ex}\n{GetStateSnapshot()}\n");
        }
        catch
        {
            // best effort: never take the app down logging a crash
        }
    }

    private static string GetStateSnapshot()
    {
        try
        {
            var vm = Services?.GetService<MainViewModel>();
            if (vm == null)
                return "";
            var sb = new StringBuilder();
            sb.Append("  State: Path=").Append(vm.FilePath ?? "(none)");
            sb.Append(" Tier=").Append(vm.DocumentTier);
            sb.Append(" ViewMode=").Append(vm.ViewMode);
            sb.Append(" Dirty=").Append(vm.IsDirty);
            sb.Append(" Progress=").Append(vm.OpenProgressText ?? "(none)");
            var editor = Services?.GetService<MainWindow>()?.Editor;
            if (editor?.Editor?.Document != null)
            {
                sb.Append(" Lines=").Append(editor.Editor.Document.LineCount);
                sb.Append(" Chars=").Append(editor.Editor.Document.TextLength);
            }
            sb.AppendLine();
            return sb.ToString();
        }
        catch
        {
            return "";
        }
    }

    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        services.AddLogging();

        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IEncodingDetector, EncodingDetector>();
        services.AddSingleton<IFileService, FileService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IMarkdownRenderer, MarkdownRenderer>();
        services.AddSingleton<PreviewTemplate>();
        services.AddSingleton<IImagePasterService, ImagePasterService>();
        services.AddTransient<SettingsViewModel>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();

        return services.BuildServiceProvider();
    }

    private static void RegisterEncodingProviders()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }
}
