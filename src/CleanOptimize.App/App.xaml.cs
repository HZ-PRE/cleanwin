using System.Windows;
using CleanOptimize.App.Services;
using CleanOptimize.App.Windows;
using CleanOptimize.Core.Rules;
using CleanOptimize.Core.SelfTest;
using CleanOptimize.Core.Store;

namespace CleanOptimize.App;

public partial class App : Application
{
    private static Mutex? _singleInstanceMutex;
    private static bool _ownsMutex;
    private NotifyIconService? _tray;

    public static new App Current => (App)Application.Current;

    protected void OnStartup(object sender, StartupEventArgs e)
    {
        // —— 提权辅助实例：执行管理任务后立即退出，不进入 UI ——
        if (e.Args.Length >= 1 && e.Args[0] == "--elevated")
        {
            var taskId = e.Args.Length > 1 ? e.Args[1] : "";
            var argsJson = e.Args.Length > 2 ? e.Args[2] : "{}";
            var resultFile = e.Args.Length > 3 ? e.Args[3] : Path.Combine(Path.GetTempPath(), "CleanOptimize_elev_result.json");
            ElevatedService.ExecuteElevatedTask(taskId, argsJson, resultFile);
            Shutdown(0);
            return;
        }

        // —— 命令行安全自测 ——
        if (e.Args.Any(a => a is "--selftest" or "--ui-selftest" or "--window-lifetime-selftest"))
        {
            StreamWriter? testLog = null;
            var logIndex = Array.IndexOf(e.Args, "--selftest-log");
            if (logIndex >= 0 && logIndex + 1 < e.Args.Length)
            {
                var logPath = e.Args[logIndex + 1];
                if (!Core.Utils.PathUtil.IsLocalPath(logPath) || Core.Utils.PathUtil.HasReparseAncestor(logPath)) { Shutdown(1); return; }
                testLog = new StreamWriter(logPath, false, System.Text.Encoding.UTF8) { AutoFlush = true };
                Console.SetOut(testLog);
                Console.SetError(testLog);
            }
            if (e.Args.Contains("--window-lifetime-selftest"))
            {
                var isolatedDir = SelfTest.CreateIsolatedEnvironment();
                Exit += (_, _) => { Core.Utils.FileUtil.DeleteDirectoryTree(isolatedDir); testLog?.Dispose(); };
                WindowLifetimeTests.RunChild(forceClose: !e.Args.Contains("--normal-close"));
                return;
            }
            // 在工作线程运行，避免 UI 线程同步上下文与异步等待死锁
            bool uiOnly = e.Args.Contains("--ui-selftest");
            if (uiOnly) SelfTest.CreateIsolatedEnvironment();
            var code = uiOnly ? 0 : System.Threading.Tasks.Task.Run(() => SelfTest.RunAll()).GetAwaiter().GetResult();
            if (AppSelfTest.RunAll() != 0) code = 1;
            if (Path.GetFileName(SettingsService.DataDir).StartsWith("CleanOptimizeTestData_", StringComparison.Ordinal) &&
                Core.Utils.PathUtil.IsUnder(SettingsService.DataDir, Path.GetTempPath()))
                Core.Utils.FileUtil.DeleteDirectoryTree(SettingsService.DataDir);
            testLog?.Dispose();
            Shutdown(code);
            return;
        }

        // —— 单实例(语言先行,提示语随界面语言) ——
        SettingsService.Load();
        _singleInstanceMutex = new Mutex(true, "CleanOptimize_SingleInstance_2F1A", out var isNew);
        _ownsMutex = isNew;
        if (!isNew)
        {
            ThemeManager.ApplyFromSettings();
            Loc.ApplyFromSettings();
            MessageBox.Show(Loc.T("app.alreadyrunning"), Loc.T("app.title"),
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown(0);
            return;
        }

        string? quarantineError = null;
        try { QuarantineService.Instance.PurgeExpired(); }
        catch (Exception ex) { quarantineError = ex.Message; AppLog.Error("Quarantine startup", ex); }
        ThemeManager.ApplyFromSettings();
        Loc.ApplyFromSettings();
        // Core 层用户可见文案(历史记录等)走应用内语言
        Core.Store.CoreLocalizer.Translate = (key, fallback) =>
        {
            var v = TryFindResource(key) as string;
            return string.IsNullOrEmpty(v) ? fallback : v;
        };

        // 全局异常兜底：记录并提示，不闪退
        DispatcherUnhandledException += (s, args) =>
        {
            AppLog.Error("DispatcherUnhandledException", args.Exception);
            try { HistoryService.Add("Error", Loc.T("app.error.msg"), args.Exception.Message); } catch { }
            MessageBox.Show($"发生了一个内部错误：\n{args.Exception.Message}", "清理优化大师",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            args.Handled = true;
        };

        // --page <key> [--param <value>] 深链接（用于自动化验收 / 快捷入口）
        // 必须在窗口创建前解析并交给 MainWindow.Loaded 统一导航:
        // 向导 ShowDialog 会泵消息使 Loaded 提前触发,晚赋值会被默认首页导航覆盖
        PageKey? initialPage = null;
        object? initialParam = null;
        var pageIdx = Array.IndexOf(e.Args, "--page");
        if (pageIdx >= 0 && pageIdx + 1 < e.Args.Length &&
            Enum.TryParse<PageKey>(e.Args[pageIdx + 1], ignoreCase: true, out var pk))
        {
            var paramIdx = Array.IndexOf(e.Args, "--param");
            initialPage = pk;
            initialParam = paramIdx >= 0 && paramIdx + 1 < e.Args.Length ? e.Args[paramIdx + 1] : null;
        }

        var win = new MainWindow { InitialPage = initialPage, InitialParam = initialParam };
        AttachMainWindow(win);
        win.Show();
        if (!win.CanInteract) return;
        if (quarantineError != null) win.ShowToast(quarantineError, ToastType.Warning);

        _tray = new NotifyIconService(win);
        _tray.Initialize();

        // 首次启动引导
        if (!Services.FirstRunWizard.WasWelcomed())
        {
            var wizard = new Windows.FirstRunWizard { Owner = win };
            wizard.ShowDialog();
        }
        if (!win.CanInteract) return;

        // 开机自启注册
        StartupRegistration.Sync(SettingsService.Current.LaunchAtStartup);

        // 自动清理调度
        AutoCleanScheduler.Instance.Initialize(win);

    }

    internal void AttachMainWindow(MainWindow window)
    {
        MainWindow = window;
        // Hiding to tray keeps the window alive; a real close must terminate the process.
        ShutdownMode = ShutdownMode.OnMainWindowClose;
    }

    protected void OnExit(object sender, ExitEventArgs e)
    {
        _tray?.Dispose();
        AutoCleanScheduler.Instance.Stop();
        // 设置仅在用户修改时落盘，避免旧快照覆盖新配置
        if (_ownsMutex) _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
    }
}
