using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace MarkDesk.Tests;

/// <summary>
/// WPF tests create a process-wide Application and real windows/WebView2
/// instances; they must never run in parallel.
/// </summary>
[CollectionDefinition("WpfApp", DisableParallelization = true)]
public class WpfAppCollection : ICollectionFixture<WpfAppFixture>;

/// <summary>
/// Runs after the last test of the "WpfApp" collection:
///  1. kills the msedgewebview2 process tree descending from this test host
///     (WebView2's Chromium children would otherwise linger for minutes and
///     lock their user-data folders), and
///  2. deletes the test temp dirs (per-test WebView2 profiles are several MB
///     each and would otherwise accumulate forever).
/// </summary>
public class WpfAppFixture : IDisposable
{
    public void Dispose()
    {
        KillWebView2ProcessTree();
        DeleteTestTempDirs();
    }

    private static void KillWebView2ProcessTree()
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
            // best effort
        }
    }

    private static void DeleteTestTempDirs()
    {
        var dirs = new[]
        {
            Path.Combine(Path.GetTempPath(), "MarkDeskTests-wv2"),
            Path.Combine(Path.GetTempPath(), "MarkDeskTests"),
        };
        // Chromium releases file handles shortly after the kill; retry a few
        // times before giving up.
        for (var attempt = 0; attempt < 4; attempt++)
        {
            try
            {
                foreach (var dir in dirs)
                    if (Directory.Exists(dir))
                        Directory.Delete(dir, recursive: true);
                return;
            }
            catch
            {
                Thread.Sleep(500);
            }
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
}