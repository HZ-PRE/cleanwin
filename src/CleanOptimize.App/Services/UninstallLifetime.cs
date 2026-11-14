using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace CleanOptimize.App.Services;

/// <summary>Uninstall shutdown is scoped to an exact executable path, never a process name alone.</summary>
internal sealed class UninstallLifetime : IDisposable
{
    private readonly EventWaitHandle _exit;
    private readonly RegisteredWaitHandle _registration;

    internal static string EventName(string executable) => @"Local\CleanOptimize.Uninstall." +
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(executable).ToUpperInvariant())));

    internal UninstallLifetime(Action close)
    {
        _exit = new EventWaitHandle(false, EventResetMode.ManualReset, EventName(Environment.ProcessPath!));
        _exit.Reset();
        _registration = ThreadPool.RegisterWaitForSingleObject(_exit, (_, _) =>
        {
            var dispatcher = System.Windows.Application.Current.Dispatcher;
            try { if (!dispatcher.HasShutdownStarted) dispatcher.BeginInvoke(close); }
            catch (InvalidOperationException) { /* application already shut down */ }
        }, null, Timeout.Infinite, executeOnlyOnce: true);
    }

    public void Dispose()
    {
        _registration.Unregister(null);
        _exit.Dispose();
    }

    /// <summary>Run in a short-lived helper before the single-instance check. Nonzero means abort uninstall.</summary>
    internal static int RequestExit()
    {
        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable) || !Path.GetFileName(executable).Equals("CleanOptimize.exe", StringComparison.OrdinalIgnoreCase)) return 1;
        var targets = new List<Process>();
        bool inaccessible = false;
        try
        {
            foreach (var process in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(executable)))
            {
                bool retained = false;
                try
                {
                    if (process.Id == Environment.ProcessId || process.HasExited) continue;
                    // Retain a handle to this process instance so PID reuse cannot target another process.
                    _ = process.SafeHandle;
                    if (!string.Equals(process.MainModule?.FileName, executable, StringComparison.OrdinalIgnoreCase)) continue;
                    targets.Add(process);
                    retained = true;
                }
                catch (InvalidOperationException) { /* process exited during enumeration */ }
                catch (System.ComponentModel.Win32Exception) { inaccessible = true; }
                finally { if (!retained) process.Dispose(); }
            }
            if (targets.Count == 0) return inaccessible ? 2 : 0;
            try
            {
                if (EventWaitHandle.TryOpenExisting(EventName(executable), out var signal))
                    using (signal) signal.Set();
                else
                    foreach (var process in targets) if (!process.HasExited) process.CloseMainWindow();
            }
            catch (UnauthorizedAccessException) { /* the exact-path fallback below can still succeed */ }
            catch (InvalidOperationException) { }

            var deadline = Stopwatch.StartNew();
            foreach (var process in targets)
            {
                if (process.WaitForExit(Math.Max(0, 8000 - (int)deadline.ElapsedMilliseconds))) continue;
                // Old versions or an unresponsive UI may ignore graceful shutdown.
                // Only terminate the already-verified process, never its children or unrelated portable copies.
                if (!string.Equals(process.MainModule?.FileName, executable, StringComparison.OrdinalIgnoreCase)) return 3;
                process.Kill(entireProcessTree: false);
                if (!process.WaitForExit(5000)) return 4;
            }
            return inaccessible ? 2 : 0;
        }
        catch (Exception) { return 5; }
        finally { foreach (var process in targets) process.Dispose(); }
    }
}
