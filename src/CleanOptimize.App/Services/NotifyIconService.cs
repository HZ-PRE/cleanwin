using System.Reflection;
using System.Windows;
using CleanOptimize.Core.Store;
using CleanOptimize.App.Controls;

namespace CleanOptimize.App.Services;

/// <summary>系统托盘图标与右键菜单；语言切换时重建菜单。</summary>
public sealed class NotifyIconService : IDisposable
{
    private readonly MainWindow _main;
    private System.Windows.Forms.NotifyIcon? _icon;
    private TrayMenu? _menu;
    private bool _disposed;

    public NotifyIconService(MainWindow main)
    {
        _main = main;
        _main.Closed += MainClosed;
    }

    private void MainClosed(object? sender, EventArgs e) => Dispose();

    internal bool OpenMainWindow() => !_disposed && _main.RestoreFromTray();

    public void Initialize()
    {
        if (_disposed || _icon != null || !_main.CanInteract) return;
        _icon = new System.Windows.Forms.NotifyIcon
        {
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!),
            Text = ToolTipText(),
            Visible = true,
        };
        BuildMenu();
        _icon.DoubleClick += (s, e) => OpenMainWindow();

        Loc.LanguageChanged += RebuildOnLanguageChanged;
        ThemeManager.ThemeChanged += RefreshTheme;
        AutoCleanScheduler.Instance.Completed += ShowAutoCleanResult;
    }

    private void ShowAutoCleanResult(Core.Models.CleanSummary summary)
    {
        if (_disposed || !_main.CanInteract || !SettingsService.Current.NotifyAutoCleanResult) return;
        var message = AutoCleanMessage(summary);
        bool warning = summary.FailCount > 0 || summary.SkipCount > 0;
        if (_main.IsVisible && _main.WindowState != WindowState.Minimized)
            _main.ShowToast(message, warning ? ToastType.Warning : ToastType.Success);
        else
            _icon?.ShowBalloonTip(5000, Loc.T("ui.autoclean.title"), message,
                warning ? System.Windows.Forms.ToolTipIcon.Warning : System.Windows.Forms.ToolTipIcon.Info);
    }

    internal static string AutoCleanMessage(Core.Models.CleanSummary summary) => string.Format(Loc.T("ui.autoclean.result"),
        Core.Utils.SizeText.OfBytes(summary.FreedBytes), Core.Utils.SizeText.OfBytes(summary.QuarantinedBytes),
        summary.SuccessCount + summary.QuarantineCount, summary.SkipCount, summary.FailCount);

    private static string ToolTipText()
    {
        var v = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion.Split('+')[0];
        return string.IsNullOrWhiteSpace(v) ? "CleanOptimize" : $"CleanOptimize v{v}";
    }

    private void BuildMenu()
    {
        var old = _menu;
        var menu = new TrayMenu();
        menu.AddAction(Loc.T("tray.open"), TrayAction.Open, (s, e) => OpenMainWindow());
        menu.AddAction(Loc.T("tray.quickscan"), TrayAction.Scan, (s, e) =>
        {
            if (!OpenMainWindow()) return;
            _main.Navigate(PageKey.SmartScan);
            if (_main.PageHost.Content is Pages.SmartScanPage p) p.StartScanFromUi();
        });
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.AddAction(Loc.T("ui.quick.title"), TrayAction.QuickClean, (_, _) =>
        {
            if (OpenMainWindow()) QuickCleanPlan.Open(_main);
        });
        var autoItem = menu.AddAction(AutoCleanText(), TrayAction.AutoClean, null);
        autoItem.Checked = SettingsService.Current.AutoCleanEnabled;
        autoItem.Click += (s, e) =>
        {
            if (_disposed || !_main.CanInteract) return;
            SettingsService.Current.AutoCleanEnabled = !SettingsService.Current.AutoCleanEnabled;
            autoItem.Checked = SettingsService.Current.AutoCleanEnabled;
            autoItem.Text = AutoCleanText();
            SettingsService.Save();
        };
        menu.Opening += (_, _) =>
        {
            autoItem.Checked = SettingsService.Current.AutoCleanEnabled;
            autoItem.Text = AutoCleanText();
        };
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.AddAction(Loc.T("tray.exit"), TrayAction.Exit, (s, e) => _main.ForceClose());

        _menu = menu;
        if (_icon != null) _icon.ContextMenuStrip = menu;
        if (_icon != null) _icon.Text = ToolTipText();
        old?.Dispose();
    }

    private void RebuildOnLanguageChanged()
    {
        if (_disposed || _icon == null) return;
        // 托盘与主窗口同线程,语言切换事件即在 UI 线程触发
        BuildMenu();
    }

    private void RefreshTheme() => _menu?.ApplyTheme();

    internal static string AutoCleanText() =>
        Loc.T("tray.autoclean") + (SettingsService.Current.AutoCleanEnabled ? Loc.T("tray.on") : Loc.T("tray.off"));

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _main.Closed -= MainClosed;
        Loc.LanguageChanged -= RebuildOnLanguageChanged;
        ThemeManager.ThemeChanged -= RefreshTheme;
        AutoCleanScheduler.Instance.Completed -= ShowAutoCleanResult;
        if (_icon != null)
        {
            var image = _icon.Icon;
            _icon.Visible = false;
            _icon.Dispose();
            image?.Dispose();
            _icon = null;
        }
        _menu?.Dispose();
        _menu = null;
    }
}
