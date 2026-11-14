using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using CleanOptimize.App.Pages;
using CleanOptimize.Core.Store;

namespace CleanOptimize.App;

public enum PageKey { Home, SmartScan, CleanSpace, SpaceAnalysis, Startup, Apps, Toolbox, Settings }

public partial class MainWindow : Window
{
    private readonly Dictionary<PageKey, Page> _pages = new();
    private PageKey _currentKey;
    private DispatcherTimer? _toastTimer;
    private bool _dragMoving;
    private bool _hasShown;
    private bool _isClosing;
    internal bool IsClosed { get; private set; }
    internal bool CanInteract => !IsClosed && !_isClosing && !Dispatcher.HasShutdownStarted;
    private readonly Services.UpdateService _updates;
    private readonly CancellationTokenSource _updateCancellation = new();
    private Task? _updateTask;
    internal Services.UpdateResult UpdateState { get; private set; } = new(Services.UpdateOutcome.NotChecked);
    internal bool CheckingUpdates { get; private set; }
    internal bool UpdateInProgress { get; private set; }
    internal event Action? UpdateStateChanged;
    internal bool HasActiveOperations => Services.AutoCleanScheduler.Instance.IsRunning || _pages.Values.Any(page => page switch
    {
        SmartScanPage scan => scan.OperationActive,
        CleanSpacePage clean => clean.OperationActive,
        SpaceAnalysisPage space => space.OperationActive,
        ToolboxPage tools => tools.OperationActive,
        AppsPage apps => apps.OperationActive,
        _ => false,
    });

    /// <summary>启动深链指定的初始页(未设置则首页),由 Loaded 统一导航,避免竞态覆盖。</summary>
    public PageKey? InitialPage { get; set; }
    public object? InitialParam { get; set; }

    public MainWindow() : this(new Services.UpdateService()) { }

    internal MainWindow(Services.UpdateService updates)
    {
        _updates = updates;
        InitializeComponent();
        var v = System.Reflection.Assembly.GetEntryAssembly()?
                .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()?
                .InformationalVersion ?? "1.0.0";
        VersionText.Text = "v" + v.Split('+')[0];
        Loaded += (s, e) =>
        {
            if (_hasShown || !CanInteract) return;
            _hasShown = true;
            ApplyWindowChromePadding();
            if (InitialPage is PageKey key) Navigate(key, InitialParam);
            else Navigate(PageKey.Home);
        };
        StateChanged += (s, e) => ApplyWindowChromePadding();
        SourceInitialized += (s, e) => RestoreWindowBounds();
        PreviewKeyDown += MainWindow_PreviewKeyDown;
        PageHost.Navigated += (_, _) =>
        {
            while (PageHost.NavigationService.RemoveBackEntry() != null) { }
        };
        Closed += (_, _) =>
        {
            IsClosed = true;
            _updateCancellation.Cancel();
            UpdateStateChanged = null;
            _toastTimer?.Stop();
            foreach (var page in _pages.Values)
            {
                if (page is SmartScanPage scan) scan.TryCancelCurrent();
                if (page is CleanSpacePage clean) clean.TryCancelCurrent();
                if (page is SpaceAnalysisPage space) space.TryCancelCurrent();
                if (page is ToolboxPage tools) tools.TryCancelCurrent();
            }
        };
    }

    // ————— 窗口位置记忆 —————
    internal Task CheckForUpdatesAsync(bool forceRefresh = false) => !CanInteract ? Task.CompletedTask :
        _updateTask is { IsCompleted: false } ? _updateTask : _updateTask = CheckUpdatesCoreAsync(forceRefresh);

    private async Task CheckUpdatesCoreAsync(bool forceRefresh)
    {
        CheckingUpdates = true;
        UpdateStateChanged?.Invoke();
        try
        {
            var result = await _updates.CheckAsync(_updateCancellation.Token, forceRefresh);
            if (CanInteract) ApplyUpdateResult(result);
        }
        catch (OperationCanceledException) { }
        finally
        {
            CheckingUpdates = false;
            if (CanInteract) UpdateStateChanged?.Invoke();
        }
    }

    internal void ApplyUpdateResult(Services.UpdateResult result)
    {
        if (!CanInteract) return;
        UpdateState = result;
        NewUpdateBadge.Visibility = result.HasUpdate ? Visibility.Visible : Visibility.Collapsed;
        NewUpdateBadge.ToolTip = result.HasUpdate ? "v" + result.Version : null;
        UpdateStateChanged?.Invoke();
    }

    private void NewUpdate_Click(object sender, RoutedEventArgs e) => ShowUpdateDialog();
    internal void ShowUpdateDialog()
    {
        if (!CanInteract || !UpdateState.HasUpdate || UpdateInProgress) return;
        UpdateInProgress = true;
        try { new Windows.UpdateWindow(this, UpdateState).ShowDialog(); }
        finally { UpdateInProgress = false; }
    }


    private void RestoreWindowBounds()
    {
        var s = SettingsService.Current;
        try
        {
            if (s.WindowW is double width && double.IsFinite(width) && width >= MinWidth &&
                s.WindowH is double height && double.IsFinite(height) && height >= MinHeight &&
                s.WindowX is double x && double.IsFinite(x) && s.WindowY is double y && double.IsFinite(y))
            {
                // 仅当落点在可见虚拟屏幕内才恢复,避免恢复到已拔掉的显示器
                var wa = SystemParameters.WorkArea;
                bool onScreen = x < SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - 100 &&
                                y < SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 100 &&
                                x + 100 > SystemParameters.VirtualScreenLeft &&
                                y + 100 > SystemParameters.VirtualScreenTop;
                if (onScreen)
                {
                    WindowStartupLocation = WindowStartupLocation.Manual;
                    Left = x; Top = y;
                    Width = width; Height = height;
                }
            }
            if (s.WindowMaximized) WindowState = WindowState.Maximized;
        }
        catch { }
    }

    private void SaveWindowBounds()
    {
        try
        {
            var s = SettingsService.Current;
            if (WindowState == WindowState.Minimized) return;
            s.WindowMaximized = WindowState == WindowState.Maximized;
            if (!s.WindowMaximized)
            {
                s.WindowX = Left; s.WindowY = Top;
                s.WindowW = Width; s.WindowH = Height;
            }
            SettingsService.Save();
        }
        catch { }
    }

    // ————— 全局快捷键：Ctrl+1..8 切页 / Ctrl+K 命令面板 / F5 刷新 / Esc 停止扫描或清理 —————

    private void MainWindow_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.K && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            Palette.Toggle();
            e.Handled = true;
        }
        else if (Palette.IsOpen && e.Key == Key.Escape)
        {
            Palette.Close();
            e.Handled = true;
        }
        else if (e.Key >= Key.D1 && e.Key <= Key.D8 &&
            (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            var key = (PageKey)(e.Key - Key.D1);
            if (Enum.IsDefined(key) && key != _currentKey)
            {
                Navigate(key);
                e.Handled = true;
            }
        }
        else if (e.Key == Key.F5)
        {
            if (_pages.TryGetValue(_currentKey, out var p) && p is IParamPage pp)
                pp.OnNavigated(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            if (_pages.TryGetValue(PageKey.SmartScan, out var p) && p is SmartScanPage sp)
                sp.TryCancelCurrent();
            if (_pages.TryGetValue(PageKey.CleanSpace, out var c) && c is CleanSpacePage cp)
                cp.TryCancelCurrent();
            if (_pages.TryGetValue(PageKey.SpaceAnalysis, out var a) && a is SpaceAnalysisPage ap)
                ap.TryCancelCurrent();
            if (_pages.TryGetValue(PageKey.Toolbox, out var t) && t is ToolboxPage tp)
                tp.TryCancelCurrent();
        }
    }

    // ————— 命令面板动作分发 —————

    private void Palette_ActionInvoked(Controls.CommandItem item)
    {
        if (item.Id.StartsWith("page.") && Enum.TryParse<PageKey>(item.Id["page.".Length..], out var key))
        {
            Navigate(key);
            return;
        }
        switch (item.Id)
        {
            case "act.quickclean":
                Services.QuickCleanPlan.Open(this);
                break;
            case "act.quickscan":
                Navigate(PageKey.SmartScan, "start");
                break;
            case "act.analyze":
                Navigate(PageKey.SpaceAnalysis, "analyze");
                break;
            case "act.dupscan":
                Navigate(PageKey.Toolbox, "dup");
                break;
            case "act.emptyscan":
                Navigate(PageKey.Toolbox, "empty");
                break;
            case "act.quarantine":
                Navigate(PageKey.Toolbox, "quarantine");
                break;
            case "act.history":
                Navigate(PageKey.Toolbox, "history");
                break;
            case "act.dns":
                Services.QuickActions.FlushDns(this);
                break;
            case "act.theme":
                Services.QuickActions.ToggleTheme(this);
                break;
            case "act.update":
                Services.QuickActions.CheckUpdate(this);
                break;
        }
    }

    private void ApplyWindowChromePadding()
    {
        // 无边框窗口最大化时留出任务栏边缘
        Padding = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
        var g = FindName("glyph") as System.Windows.Shapes.Path;
        if (g != null)
            g.Data = (Geometry)FindResource(WindowState == WindowState.Maximized ? "RestoreGlyph" : "MaxGlyph");
    }

    // ————— 导航 —————

    public void Navigate(PageKey key, object? param = null)
    {
        if (!CanInteract) return;
        if (!_pages.TryGetValue(key, out var page))
        {
            page = key switch
            {
                PageKey.Home => new HomePage(this),
                PageKey.SmartScan => new SmartScanPage(this),
                PageKey.CleanSpace => new CleanSpacePage(this),
                PageKey.SpaceAnalysis => new SpaceAnalysisPage(this),
                PageKey.Startup => new StartupPage(this),
                PageKey.Apps => new AppsPage(this),
                PageKey.Toolbox => new ToolboxPage(this),
                PageKey.Settings => new SettingsPage(this),
                _ => new HomePage(this),
            };
            _pages[key] = page;
        }
        // Avoid reattaching the same page or keeping navigation journal references.
        if (!ReferenceEquals(PageHost.Content, page)) PageHost.Content = page;
        // 先挂载内容再通知页面（保证 BringIntoView 等操作生效）
        if (page is IParamPage pp) pp.OnNavigated(param);
        // 深链：--param palette 打开命令面板（自动化验收用）
        if (param as string == "palette")
            Dispatcher.BeginInvoke(() => Palette.Open(), System.Windows.Threading.DispatcherPriority.Loaded);
        _currentKey = key;
        CleanupHeader.Visibility = key is PageKey.SmartScan or PageKey.CleanSpace ? Visibility.Visible : Visibility.Collapsed;
        var selectedTab = key == PageKey.SmartScan ? "smart" :
            page is CleanSpacePage cp ? cp.SelectedTab.ToString() : "";
        foreach (var tab in CleanupTabs.Children.OfType<RadioButton>())
            tab.IsChecked = tab.Tag as string == selectedTab;
        WindowTitleText.Text = key switch
        {
            PageKey.Home => Services.Loc.T("nav.home"),
            PageKey.SmartScan => Services.Loc.T("nav.scan"),
            PageKey.CleanSpace => Services.Loc.T("nav.clean"),
            PageKey.SpaceAnalysis => Services.Loc.T("nav.space"),
            PageKey.Startup => Services.Loc.T("nav.startup"),
            PageKey.Apps => Services.Loc.T("nav.apps"),
            PageKey.Toolbox => Services.Loc.T("nav.toolbox"),
            PageKey.Settings => Services.Loc.T("nav.settings"),
            _ => "",
        };
        SyncNavChecked(key);
    }

    private void SyncNavChecked(PageKey key)
    {
        if (key == PageKey.CleanSpace) key = PageKey.SmartScan;
        foreach (var rb in new[] { NavHome, NavScan, NavSpace, NavStartup, NavApps, NavToolbox, NavSettings })
        {
            if ((string)rb.Tag == key.ToString())
            {
                rb.IsChecked = true;
            }
        }
    }

    private void CleanupTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton button) return;
        if (button.Tag as string == "smart") Navigate(PageKey.SmartScan);
        else if (int.TryParse(button.Tag as string, out var tab)) Navigate(PageKey.CleanSpace, tab);
    }

    private void Nav_Click(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && Enum.TryParse<PageKey>((string)rb.Tag, out var key) && key != _currentKey)
            Navigate(key);
    }

    // ————— Toast —————

    public void ShowToast(string message, ToastType type = ToastType.Success)
    {
        if (!CanInteract) return;
        ToastText.Text = message;
        ToastIcon.Data = (Geometry)FindResource(
            type == ToastType.Success ? "CheckIcon" :
            type == ToastType.Warning ? "WarningIcon" : "InfoIcon");
        ToastIcon.Stroke = type switch
        {
            ToastType.Success => (Brush)FindResource("SuccessBrush"),
            ToastType.Warning => (Brush)FindResource("WarningBrush"),
            _ => (Brush)FindResource("PrimaryBrush"),
        };
        Toast.Visibility = Visibility.Visible;
        Toast.Opacity = 0;
        var slide = new TranslateTransform(0, -10);
        Toast.RenderTransform = slide;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        Toast.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)) { EasingFunction = ease });
        slide.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(-10, 0, TimeSpan.FromMilliseconds(240)) { EasingFunction = ease });

        _toastTimer?.Stop();
        _toastTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.6) };
        _toastTimer.Tick += (s, e) =>
        {
            _toastTimer!.Stop();
            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(300));
            fadeOut.Completed += (s2, e2) => Toast.Visibility = Visibility.Collapsed;
            Toast.BeginAnimation(OpacityProperty, fadeOut);
        };
        _toastTimer.Start();
    }

    // ————— 标题栏 —————

    private bool _downInDragArea;
    private Point _downPoint;

    private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }
        _downInDragArea = true;
        _downPoint = e.GetPosition(this);
    }

    private void TitleBar_MouseUp(object sender, MouseButtonEventArgs e)
    {
        _downInDragArea = false;
        _dragMoving = false;
    }

    private void TitleBar_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_downInDragArea || e.LeftButton != MouseButtonState.Pressed) { _dragMoving = false; return; }
        // 离开阈值后开始拖动（避免吞掉按钮点击）
        var pos = e.GetPosition(this);
        if (!_dragMoving && Math.Abs(pos.X - _downPoint.X) + Math.Abs(pos.Y - _downPoint.Y) < 6) return;
        _dragMoving = true;
        if (WindowState == WindowState.Maximized)
        {
            // 从最大化拖出：恢复并跟随鼠标
            var pctX = pos.X / ActualWidth;
            ToggleMaximize();
            Left = pos.X - ActualWidth * pctX;
            Top = pos.Y - 26;
        }
        try { DragMove(); } catch { }
        _downInDragArea = false;
    }

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void BtnMin_Click(object sender, RoutedEventArgs e)
    {
        if (SettingsService.Current.MinimizeToTray)
            HideToTray();
        else
            WindowState = WindowState.Minimized;
    }

    public void HideToTray()
    {
        if (CanInteract) Hide();
    }

    public bool RestoreFromTray()
    {
        if (!CanInteract) return false;
        Show();
        WindowState = WindowState.Normal;
        if (ShowActivated) Activate();
        return true;
    }

    private void BtnMax_Click(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void BtnPaletteHint_Click(object sender, RoutedEventArgs e) => Palette.Open();

    private bool _allowClose;

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        SaveWindowBounds();
        // Close button, Alt+F4 and system close share the same tray policy.
        if (!_allowClose && _hasShown && SettingsService.Current.CloseToTray && !Dispatcher.HasShutdownStarted)
        {
            e.Cancel = true;
            base.OnClosing(e);
            if (e.Cancel) { HideToTray(); return; }
        }
        else
        {
            _isClosing = true;
            base.OnClosing(e);
        }
        _isClosing = !e.Cancel;
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        var s = SettingsService.Current;
        if (s.CloseToTray)
        {
            HideToTray();
            ShowToast(Services.Loc.T("toast.traymin"), ToastType.Info);
            return;
        }
        if (!_allowClose)
        {
            var exit = Services.DialogService.Confirm(this, Services.Loc.T("dlg.exit.title"), Services.Loc.T("dlg.exit.msg"), Services.Loc.T("btn.exit"), Services.Loc.T("btn.cancel"));
            if (!exit) return;
            _allowClose = true;
        }
        Close();
    }

    internal void ForceClose()
    {
        if (!CanInteract) return;
        _allowClose = true;
        Close();
    }
}

public enum ToastType { Success, Warning, Info }

public interface IParamPage
{
    void OnNavigated(object? param);
}
