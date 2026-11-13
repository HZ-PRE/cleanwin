using System.Diagnostics;
using System.Windows;
using CleanOptimize.Core.Store;

namespace CleanOptimize.App.Services;

/// <summary>命令面板 / 工具箱共用的快捷动作。</summary>
public static class QuickActions
{
    public static async void FlushDns(MainWindow main)
    {
        try
        {
            var psi = new ProcessStartInfo(System.IO.Path.Combine(Environment.SystemDirectory, "ipconfig"), "/flushdns")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
            };
            var p = Process.Start(psi);
            if (p != null) await p.WaitForExitAsync();
            main.ShowToast(Loc.T("tool.win.dns.ok"), ToastType.Success);
        }
        catch (Exception ex)
        {
            main.ShowToast(Loc.T("tool.win.dns.fail") + ex.Message, ToastType.Warning);
        }
    }

    public static void CheckUpdate(MainWindow main)
    {
        main.ShowToast(UpdateService.OfflineMessage, ToastType.Info);
    }

    /// <summary>浅色 ↔ 深色互切(system 视为浅色)。</summary>
    public static void ToggleTheme(MainWindow main)
    {
        var s = SettingsService.Current;
        s.Theme = s.Theme == "dark" ? "light" : "dark";
        SettingsService.Save();
        ThemeManager.ApplyFromSettings();
        main.ShowToast(Loc.T("toast.themeapplied"), ToastType.Info);
    }
}
