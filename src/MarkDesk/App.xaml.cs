using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
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

    protected override void OnExit(ExitEventArgs e)
    {
        base.OnExit(e);
        // WebView2's Chromium children (msedgewebview2.exe) can linger for a
        // minute or two after the app exits instead of dying with the parent
        // process. Terminate any of ours right away so nothing is left behind.
        KillOrphanedWebView2Children();
    }

    /// <summary>
    /// Kills the whole msedgewebview2 process tree descending from this
    /// process. Only the browser process is a direct child (renderer/gpu
    /// processes have the browser as their parent), so a descendant BFS over
    /// a toolhelp snapshot is needed. The process name is re-verified before
    /// each Kill to defend against PID reuse between snapshot and kill.
    /// </summary>
    private static void KillOrphanedWebView2Children()
    {
        try
        {
            var ownPid = (uint)Environment.ProcessId;
            var snapshot = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
            if (snapshot == INVALID_HANDLE_VALUE)
                return;
            List<(uint Pid, uint ParentPid, string Name)> processes;
            try
            {
                processes = new List<(uint, uint, string)>();
                var entry = new PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf<PROCESSENTRY32>() };
                if (!Process32First(snapshot, ref entry))
                    return;
                do
                {
                    processes.Add((entry.th32ProcessID, entry.th32ParentProcessID, entry.szExeFile));
                } while (Process32Next(snapshot, ref entry));
            }
            finally
            {
                CloseHandle(snapshot);
            }

            // BFS from this process down the tree, collecting our Chromium processes.
            var targets = new List<uint>();
            var frontier = new List<uint> { ownPid };
            while (frontier.Count > 0)
            {
                var next = new List<uint>();
                foreach (var proc in processes)
                {
                    if (!frontier.Contains(proc.ParentPid) ||
                        !string.Equals(proc.Name, "msedgewebview2.exe", StringComparison.OrdinalIgnoreCase))
                        continue;
                    targets.Add(proc.Pid);
                    next.Add(proc.Pid);
                }
                frontier = next;
            }

            foreach (var pid in targets)
            {
                try
                {
                    var process = Process.GetProcessById((int)pid);
                    if (string.Equals(process.ProcessName, "msedgewebview2", StringComparison.OrdinalIgnoreCase))
                        process.Kill();
                }
                catch
                {
                    // already exited between snapshot and kill
                }
            }
        }
        catch
        {
            // best effort: the OS reaps orphaned children eventually
        }
    }

    private const uint TH32CS_SNAPPROCESS = 0x00000002;
    private static readonly IntPtr INVALID_HANDLE_VALUE = new(-1);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PROCESSENTRY32
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);
    [DllImport("kernel32.dll")]
    private static extern bool Process32First(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);
    [DllImport("kernel32.dll")]
    private static extern bool Process32Next(IntPtr hSnapshot, ref PROCESSENTRY32 lppe);
    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr hObject);

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
