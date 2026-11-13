using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using CleanOptimize.Core.Store;

namespace CleanOptimize.App.Services;

/// <summary>Separate process so real OnMainWindowClose termination can be verified.</summary>
internal static class WindowLifetimeTests
{
    internal static void RunChild(bool forceClose)
    {
        try
        {
            SettingsService.Current.CloseToTray = true;
            SettingsService.Current.WindowW = 900;
            SettingsService.Current.WindowH = 600;
            SettingsService.Current.WindowX = SystemParameters.WorkArea.Left;
            SettingsService.Current.WindowY = SystemParameters.WorkArea.Top;
            SettingsService.Current.WindowMaximized = false;
            // Native WPF lifecycle without a visible window, taskbar button, tray icon or focus changes.
            var main = new MainWindow { Opacity = 0, ShowInTaskbar = false, ShowActivated = false };
            using var tray = new NotifyIconService(main);
            App.Current.AttachMainWindow(main);
            main.Show();
            main.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            if (main.Width != 900 || main.Height != 600) throw new InvalidOperationException("未恢复小窗口尺寸");
            for (int i = 0; i < 3; i++)
            {
                main.Close();
                if (main.IsClosed || main.IsVisible) throw new InvalidOperationException("系统关闭没有隐藏到托盘");
                if (!tray.OpenMainWindow() || !main.IsVisible) throw new InvalidOperationException("隐藏后不能恢复");
            }
            if (forceClose) main.ForceClose();
            else { SettingsService.Current.CloseToTray = false; main.Close(); }
            if (!main.IsClosed || main.CanInteract || tray.OpenMainWindow() || main.RestoreFromTray())
                throw new InvalidOperationException("实际关闭后仍可恢复窗口");
            if (SettingsService.Current.WindowW != 900 || SettingsService.Current.WindowH != 600)
                throw new InvalidOperationException("关闭后未记忆小窗口尺寸");
            main.HideToTray();
            main.ForceClose();
            main.Navigate(PageKey.Toolbox);
            main.ShowToast("late callback");
            if (DialogService.Confirm(main, "late", "late")) throw new InvalidOperationException("关闭后仍显示确认框");
            Console.WriteLine("[PASS] " + (forceClose ? "托盘退出" : "普通关闭") + "：隐藏与恢复 3 次；关闭后拒绝恢复、导航和弹窗");
            // Do not call Shutdown here: the parent verifies that the production close policy exits.
        }
        catch (Exception ex)
        {
            Console.WriteLine("[FAIL] 窗口生命周期: " + ex);
            App.Current.Shutdown(1);
        }
    }

    internal static void VerifyInChildProcess()
    {
        VerifyChild(normalClose: false);
        VerifyChild(normalClose: true);
    }

    private static void VerifyChild(bool normalClose)
    {
        var log = Path.Combine(SettingsService.DataDir, "window-lifetime-test.log");
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
        if (Path.GetFileNameWithoutExtension(start.FileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(typeof(App).Assembly.Location);
        start.ArgumentList.Add("--window-lifetime-selftest");
        if (normalClose) start.ArgumentList.Add("--normal-close");
        start.ArgumentList.Add("--selftest-log");
        start.ArgumentList.Add(log);
        try
        {
            using var child = Process.Start(start) ?? throw new InvalidOperationException("无法启动生命周期测试");
            if (!child.WaitForExit(15000))
            {
                child.Kill(); child.WaitForExit();
                throw new InvalidOperationException("主窗口关闭后进程未退出");
            }
            var result = File.Exists(log) ? File.ReadAllText(log) : "没有测试日志";
            if (child.ExitCode != 0 || !result.Contains("[PASS]") || result.Contains("[FAIL]"))
                throw new InvalidOperationException(result);
            Console.Write(result.Replace("[PASS]", "[DETAIL]"));
        }
        finally { if (File.Exists(log)) File.Delete(log); }
    }
}
