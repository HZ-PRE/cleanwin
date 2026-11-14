using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CleanOptimize.App.Services;
using CleanOptimize.Core.Analysis;
using CleanOptimize.Core.Models;
using CleanOptimize.Core.Utils;

namespace CleanOptimize.App.Pages;

public partial class HomePage : Page, IParamPage
{
    private readonly MainWindow _main;
    private readonly SystemMonitorService _monitor = new();
    private DispatcherTimer? _monitorTimer;
    private Task? _refreshTask;
    private string _selectedDrive = "";
    private HomeDashboardSnapshot? _lastDashboard;
    private bool _scrollToStats;
    private int _lastCpu = -2, _lastRam = -2;
    private long _lastRamUsed = -1, _lastRamTotal = -1, _lastUptimeMinute = -1;

    public HomePage(MainWindow main)
    {
        InitializeComponent();
        _main = main;
        CpuLabel.ToolTip = string.Format(Loc.T("home.mon.cores"), Environment.ProcessorCount);
        Loaded += (_, _) => { _ = RefreshAsync(); _main.StateChanged += WindowStateChanged; UpdateMonitorState(); };
        Unloaded += (_, _) => { _main.StateChanged -= WindowStateChanged; StopMonitor(); };
        IsVisibleChanged += (_, _) => UpdateMonitorState();
    }

    public void OnNavigated(object? param)
    {
        _scrollToStats = param as string == "stats";
        if (_scrollToStats) DetailsExpander.IsExpanded = true;
        _ = RefreshAsync();
    }

    internal Task RefreshAsync() => _refreshTask is { IsCompleted: false } ? _refreshTask : _refreshTask = RefreshCoreAsync();

    private async Task RefreshCoreAsync()
    {
        try
        {
            while (true)
            {
                var requested = _selectedDrive;
                var data = await Task.Run(() => HomeDashboardSnapshot.Load(requested));
                if (!_main.CanInteract) return;
                if (requested != _selectedDrive) continue;
                ApplyDashboard(data);
                break;
            }
            if (_scrollToStats && IsLoaded)
            {
                _scrollToStats = false;
                StatsBars.BringIntoView();
            }
        }
        catch (Exception ex) { AppLog.Error("Home.Refresh", ex); }
    }

    internal void ApplyDashboard(HomeDashboardSnapshot data)
    {
        _lastDashboard = data;
        _selectedDrive = data.SelectedDrive;
        var drive = data.Drives.Find(d => d.Name.Equals(_selectedDrive, StringComparison.OrdinalIgnoreCase));
        RenderDriveChips(data.Drives, drive);
        DiskTitle.Text = Loc.T("ui.home.disk");
        DiskFreeText.Text = drive == null ? "—" : string.Format(Loc.T("ui.home.disk.available"), SizeText.OfBytes(drive.Free, 0));
        DiskUsedText.Text = drive == null ? "—" : string.Format(Loc.T("ui.home.disk.capacity"), SizeText.OfBytes(drive.Used, 0), SizeText.OfBytes(drive.Total, 0));
        DiskPercentText.Text = drive == null ? "—" : string.Format(Loc.T("ui.home.disk.percent"), drive.UsedPercent, drive.Name.TrimEnd('\\'));
        DiskUsageBar.Value = drive?.UsedPercent ?? 0;
        DiskUsageBar.Foreground = (Brush)FindResource(drive?.UsedPercent >= 90 ? "DangerBrush" : drive?.UsedPercent >= 75 ? "WarningBrush" : "PrimaryBrush");
        DiskNoteText.Text = Loc.T(drive?.UsedPercent >= 90 ? "home.status.tight.detail" : "ui.home.disk.note");

        var scan = data.Scan;
        var junkBytes = Math.Max(0, scan?.TotalBytes ?? 0);
        LastScanText.Text = scan == null ? Loc.T("home.noscan") : string.Format(Loc.T("home.status.lastscan"), FormatTime(scan.Time));
        StatusTitle.Text = Loc.T(scan == null ? "ui.home.pending" : "home.card.junk");
        JunkSizeText.Text = scan == null ? "—" : SizeText.OfBytes(junkBytes);
        JunkNoteText.Text = Loc.T(scan == null ? "home.noscan.detail" : "ui.home.scan.review");
        ApplyHealth(scan, data.HighImpact, data.SystemFreePercent, data.RecycleBytes);

        int highImpact = data.HighImpact ?? 0;
        StartupCountText.Text = data.StartupCount == null ? "—" : string.Format(Loc.T("common.n.items"), highImpact > 0 ? highImpact : data.StartupCount);
        StartupNoteText.Text = Loc.T(highImpact > 0 ? "home.card.startup.note.high" : "home.card.startup.note");
        AppsCountText.Text = data.AppCount?.ToString() ?? "—";
        var disk = SelectedAnalysis(data);
        LargeCountText.Text = disk?.LargeFileCount is int count ? string.Format(Loc.T("common.n.items"), count)
            : Loc.T(disk == null ? "home.card.large.pending" : "home.card.large.analyzed");
        LargeNoteText.Text = disk == null ? Loc.T("home.card.large.note") : Loc.T("ui.home.large.note");
        var recent = data.History.FirstOrDefault();
        RecentActivityText.Text = recent == null ? Loc.T("home.activity.empty") : $"{recent.Title} · {FormatTime(recent.Time)}";
        // Keep only aggregate data while collapsed; generate optional rows on demand.
        if (DetailsExpander.IsExpanded) RenderDetails();
    }

    private DiskSummary? SelectedAnalysis(HomeDashboardSnapshot data) => data.Analysis is { } disk &&
        disk.Drive.Equals(_selectedDrive, StringComparison.OrdinalIgnoreCase) ? disk : null;

    private void ApplyHealth(LastScanSummary? scan, int? highImpact, double? freePct, long binBytes)
    {
        var report = scan != null && highImpact.HasValue && freePct.HasValue
            ? HealthScoreService.Compute(Math.Max(0, scan.TotalBytes), highImpact.Value, freePct.Value, binBytes) : null;
        HealthScoreText.Text = report?.Score.ToString() ?? "—";
        HealthLevelText.Text = Loc.T(report?.LevelKey ?? "ui.home.pending");
        HealthDot.Fill = (Brush)FindResource(report?.BrushKey ?? "TextTertiaryBrush");
    }

    private void Details_Expanded(object sender, RoutedEventArgs e) => RenderDetails();

    private void Details_Collapsed(object sender, RoutedEventArgs e)
    {
        CategoryList.ItemsSource = null;
        StatsBars.Children.Clear();
        SuggestList.ItemsSource = null;
    }

    private void RenderDetails()
    {
        if (_lastDashboard is not { } data) return;
        var disk = SelectedAnalysis(data);
        var categories = disk?.Categories.Take(6).Select(c => new HomeCategoryRow(Loc.CategoryDisplay(c.Name),
            SizeText.OfBytes(Math.Max(0, c.Bytes), 0),
            $"{(disk.UsedBytes > 0 ? Math.Clamp(c.Bytes * 100.0 / disk.UsedBytes, 0, 100) : 0):F1}%")).ToList();
        CategoryList.ItemsSource = categories;
        AnalysisEmpty.Visibility = categories is { Count: > 0 } ? Visibility.Collapsed : Visibility.Visible;
        AnalysisNote.Text = disk == null ? _selectedDrive : string.Format(Loc.T("ui.home.analysis.note"), disk.Drive.TrimEnd('\\'), FormatTime(disk.Time));
        BuildStats(data.History);
        var suggests = new List<SuggestItem>(3);
        if (data.Scan is { } scan)
        {
            foreach (var rule in scan.Rules.Where(r => r.Size > 200L * 1024 * 1024).OrderByDescending(r => r.Size).Take(2))
                suggests.Add(new SuggestItem { Name = Loc.RuleName(rule.Id, rule.Name), Note = Loc.T("ui.home.review"),
                    SizeText = Loc.T("common.approx") + SizeText.OfBytes(rule.Size),
                    ActionText = Loc.T(rule.Risk <= (int)RiskLevel.Low && rule.Group <= (int)RuleGroup.AppCache ? "ui.quick.title" : "ui.home.review.items"),
                    RuleId = rule.Risk <= (int)RiskLevel.Low && rule.Group <= (int)RuleGroup.AppCache ? rule.Id : null });
        }
        if (data.HighImpact is > 0)
            suggests.Add(new SuggestItem { Name = Loc.T("home.suggest.startup.title"), Note = string.Format(Loc.T("home.suggest.startup.note"), data.HighImpact),
                SizeText = string.Format(Loc.T("common.n.items"), data.HighImpact), ActionText = Loc.T("home.suggest.optimize"), IsStartup = true });
        SuggestList.ItemsSource = suggests;
        NoSuggestText.Visibility = suggests.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BuildStats(List<ActivityRecord> history)
    {
        long total = 0;
        int count = 0;
        var today = DateTime.Today;
        var days = new long[7];
        foreach (var item in history)
        {
            if (item.Type is not ("Clean" or "AutoClean")) continue;
            count++;
            var bytes = Math.Max(0, item.Bytes);
            total += bytes;
            int age = (today - item.Time.Date).Days;
            if (age is >= 0 and < 7) days[6 - age] += bytes;
        }
        StatsTotalText.Text = string.Format(Loc.T("home.stats.total"), SizeText.OfBytes(total, 0), count);
        long max = Math.Max(1, days.Max());
        StatsBars.Children.Clear();
        for (int i = 0; i < days.Length; i++)
            StatsBars.Children.Add(new Border
            {
                Height = Math.Max(3, 38.0 * days[i] / max), CornerRadius = new CornerRadius(3),
                Background = (Brush)FindResource(days[i] > 0 ? "PrimaryBrush" : "TrackBrush"),
                Opacity = i == 6 || days[i] == 0 ? 1 : 0.5,
                Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Bottom,
                ToolTip = $"{today.AddDays(i - 6):MM-dd} · {SizeText.OfBytes(days[i], 0)}",
            });
    }

    internal bool MonitorRunning => _monitorTimer?.IsEnabled == true;
    private void WindowStateChanged(object? sender, EventArgs e) => UpdateMonitorState();
    private void UpdateMonitorState()
    {
        if (IsLoaded && IsVisible && _main.IsVisible && _main.WindowState != WindowState.Minimized && _main.CanInteract) StartMonitor();
        else StopMonitor();
    }

    private void StartMonitor()
    {
        if (MonitorRunning) return;
        if (_monitorTimer == null)
        {
            _monitorTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(2) };
            _monitorTimer.Tick += (_, _) => UpdateMonitor();
        }
        _monitor.Reset();
        _monitorTimer.Start();
        UpdateMonitor();
    }

    private void StopMonitor() => _monitorTimer?.Stop();
    internal void UpdateMonitor()
    {
        try { ApplyMonitorSnapshot(_monitor.Sample()); }
        catch { }
    }

    internal void ApplyMonitorSnapshot(SystemMonitorService.Snapshot snapshot)
    {
        int cpu = snapshot.CpuPercent is double value && double.IsFinite(value) ? (int)Math.Round(Math.Clamp(value, 0, 100)) : -1;
        int ram = snapshot.RamTotalBytes > 0 ? (int)Math.Round(Math.Clamp(snapshot.RamPercent, 0, 100)) : -1;
        if (cpu != _lastCpu)
        {
            _lastCpu = cpu;
            CpuPercentText.Text = cpu < 0 ? "—" : $"{cpu}%";
            CpuBar.Value = Math.Max(0, cpu);
            CpuBar.Foreground = LoadBrush(cpu);
        }
        if (ram != _lastRam)
        {
            _lastRam = ram;
            RamPercentText.Text = ram < 0 ? "—" : $"{ram}%";
            RamBar.Value = Math.Max(0, ram);
            RamBar.Foreground = LoadBrush(ram);
        }
        if (_lastRamUsed != snapshot.RamUsedBytes || _lastRamTotal != snapshot.RamTotalBytes)
        {
            _lastRamUsed = snapshot.RamUsedBytes; _lastRamTotal = snapshot.RamTotalBytes;
            RamDetailText.Text = ram < 0 ? "—" : string.Format(Loc.T("home.mon.ram.f"), SizeText.OfBytes(snapshot.RamUsedBytes, 0), SizeText.OfBytes(snapshot.RamTotalBytes, 0));
        }
        var minute = (long)snapshot.Uptime.TotalMinutes;
        if (_lastUptimeMinute != minute) { _lastUptimeMinute = minute; UptimeText.Text = FormatUptime(snapshot.Uptime); }
    }

    private Brush LoadBrush(int percent) => (Brush)FindResource(percent >= 90 ? "DangerBrush" : percent >= 75 ? "WarningBrush" : "PrimaryBrush");

    private void RenderDriveChips(List<HomeDashboardSnapshot.Disk> all, HomeDashboardSnapshot.Disk? selected)
    {
        DriveChipHost.Children.Clear();
        foreach (var drive in all)
        {
            var chip = new RadioButton
            {
                Style = (Style)FindResource("PillTab"), GroupName = "homedrive",
                Content = drive.Name.TrimEnd(System.IO.Path.DirectorySeparatorChar), FontSize = 11,
                Padding = new Thickness(9, 2, 9, 3), Margin = new Thickness(0, 0, 5, 0),
                IsChecked = drive.Name.Equals(selected?.Name, StringComparison.OrdinalIgnoreCase),
            };
            var name = drive.Name;
            chip.Checked += (_, _) => { if (name != _selectedDrive) { _selectedDrive = name; _ = RefreshAsync(); } };
            DriveChipHost.Children.Add(chip);
        }
    }

    private static string FormatUptime(TimeSpan time)
    {
        if (time.TotalDays >= 1) return string.Format(Loc.T("home.mon.uptime.d"), (int)time.TotalDays, time.Hours);
        if (time.TotalHours >= 1) return string.Format(Loc.T("home.mon.uptime.h"), (int)time.TotalHours, time.Minutes);
        return string.Format(Loc.T("home.mon.uptime.m"), Math.Max(1, (int)time.TotalMinutes));
    }

    internal static string FormatTime(DateTime time)
    {
        var elapsed = DateTime.Now - time;
        if (elapsed.TotalMinutes < 1) return Loc.T("time.now");
        if (elapsed.TotalHours < 1) return string.Format(Loc.T("time.minago"), (int)elapsed.TotalMinutes);
        if (elapsed.TotalDays < 1) return string.Format(Loc.T("time.today"), time.ToString("HH:mm"));
        if (elapsed.TotalDays < 2) return string.Format(Loc.T("time.yesterday"), time.ToString("HH:mm"));
        return time.ToString("MM-dd HH:mm");
    }

    private void BtnScan_Click(object sender, RoutedEventArgs e) => _main.Navigate(PageKey.SmartScan, "start");
    private void BtnQuickClean_Click(object sender, RoutedEventArgs e) => QuickCleanPlan.Open(_main);
    private void Health_Click(object sender, RoutedEventArgs e) => _main.Navigate(PageKey.SmartScan);
    private void CardJunk_Click(object sender, RoutedEventArgs e) => _main.Navigate(PageKey.SmartScan);
    private void CardStartup_Click(object sender, RoutedEventArgs e) => _main.Navigate(PageKey.Startup);
    private void CardLarge_Click(object sender, RoutedEventArgs e) => OpenAnalysis(true);
    private void CardApps_Click(object sender, RoutedEventArgs e) => _main.Navigate(PageKey.Apps);
    private void BtnAnalyze_Click(object sender, RoutedEventArgs e) => OpenAnalysis();
    private void OpenAnalysis(bool large = false) => _main.Navigate(PageKey.SpaceAnalysis, new SpaceAnalysisPage.DriveRequest(_selectedDrive, large));
    private void BtnHistory_Click(object sender, RoutedEventArgs e) => _main.Navigate(PageKey.Toolbox, "history");
    private void SuggestAction_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: SuggestItem item }) return;
        if (item.IsStartup) _main.Navigate(PageKey.Startup);
        else if (item.RuleId != null) QuickCleanPlan.Open(_main, item.RuleId);
        else _main.Navigate(PageKey.SmartScan);
    }
}

internal sealed record HomeCategoryRow(string Name, string SizeText, string PercentText);

public sealed class SuggestItem
{
    public string Name { get; set; } = "";
    public string Note { get; set; } = "";
    public string SizeText { get; set; } = "";
    public string ActionText { get; set; } = "立即清理";
    public bool IsStartup { get; set; }
    public string? RuleId { get; set; }
}
