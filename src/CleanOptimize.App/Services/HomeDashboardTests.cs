using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CleanOptimize.App.Controls;
using CleanOptimize.App.Pages;
using CleanOptimize.Core.Analysis;
using CleanOptimize.Core.Models;
using CleanOptimize.Core.Utils;
using Panel = System.Windows.Controls.Panel;
using ProgressBar = System.Windows.Controls.ProgressBar;

namespace CleanOptimize.App.Services;

internal static class HomeDashboardTests
{
    internal static HomeDashboardSnapshot Fixture(long junk = 12L * 1024 * 1024 * 1024) => new()
    {
        SelectedDrive = @"C:\", Drives = [new(@"C:\", 1000L * 1024 * 1024 * 1024, 400L * 1024 * 1024 * 1024), new(@"F:\", 2000, 1500)],
        Scan = new LastScanSummary { TotalBytes = junk, Time = DateTime.Now.AddMinutes(-20), Rules = [new() { Id = "temp.user", Name = "临时文件", Size = junk }] },
        SystemFreePercent = 40, StartupCount = 6, HighImpact = 1, AppCount = 217,
        Analysis = new DiskSummary { Drive = @"C:\", LargeFileCount = 24, UsedBytes = 600L * 1024 * 1024 * 1024, Time = DateTime.Now.AddHours(-2),
            Categories = new[] { ("应用", 230, "#0F968A"), ("其他", 120, "#61738E"), ("文档", 100, "#C52C83"),
                ("系统", 80, "#3376CE"), ("音频", 40, "#7655BB"), ("视频", 30, "#C15B16") }
                .Select(c => new DiskSummary.CatItem { Name = c.Item1, Bytes = (long)c.Item2 * 1024 * 1024 * 1024, Hex = c.Item3 }).ToList() },
        History = [new ActivityRecord { Type = "Clean", Title = "删除了 512 MB", Bytes = 512L * 1024 * 1024, Time = DateTime.Now.AddHours(-1) },
            new ActivityRecord { Type = "Restore", Title = "恢复了 1 个隔离项目", Bytes = 99L * 1024 * 1024 * 1024, Time = DateTime.Now.AddHours(-2) }],
    };

    internal static void DataStates()
    {
        var main = new MainWindow();
        try
        {
            var page = new HomePage(main);
            TextBlock Text(string name) => (TextBlock)page.FindName(name);
            page.ApplyDashboard(Fixture());
            if (Text("LargeCountText").Text != string.Format(Loc.T("common.n.items"), 24))
                throw new InvalidOperationException("大文件未使用上次分析摘要");
            if (Text("HealthScoreText").Text != HealthScoreService.Compute(12L * 1024 * 1024 * 1024, 1, 40, 0).Score.ToString())
                throw new InvalidOperationException("评分未使用现有公式");
            ((Expander)page.FindName("DetailsExpander")).IsExpanded = true;
            if (!Text("StatsTotalText").Text.Contains(SizeText.OfBytes(512L * 1024 * 1024, 0)))
                throw new InvalidOperationException("恢复记录被计入清理量");
            var first = (HomeCategoryRow)((ItemsControl)page.FindName("CategoryList")).Items[0];
            if (first.PercentText != $"{230.0 / 600 * 100:F1}%")
                throw new InvalidOperationException("分类占比计算错误");
            page.ApplyDashboard(Fixture(0));
            if (Text("JunkSizeText").Text != SizeText.OfBytes(0)) throw new InvalidOperationException("零值残留上次扫描数字");
            page.ApplyDashboard(new HomeDashboardSnapshot { AppCount = 0, StartupCount = 0, HighImpact = 0 });
            if (Text("JunkSizeText").Text != "—" || Text("AppsCountText").Text != "0" || Text("HealthScoreText").Text != "—" ||
                Text("HealthLevelText").Text != Loc.T("ui.home.pending") || ((ItemsControl)page.FindName("CategoryList")).Items.Count != 0 ||
                ((FrameworkElement)page.FindName("AnalysisEmpty")).Visibility != Visibility.Visible)
                throw new InvalidOperationException("未知数据被伪造为健康、零应用显示错误或旧分类未清除");
        }
        finally { main.ForceClose(); }
    }

    internal static void SelectedDisk()
    {
        var main = new MainWindow();
        try
        {
            var page = new HomePage(main);
            var data = Fixture();
            ((Expander)page.FindName("DetailsExpander")).IsExpanded = true;
            page.ApplyDashboard(new HomeDashboardSnapshot { SelectedDrive = @"F:\", Drives = data.Drives, Analysis = data.Analysis });
            if (!((TextBlock)page.FindName("DiskPercentText")).Text.Contains("F:") || ((ProgressBar)page.FindName("DiskUsageBar")).Value != 25 ||
                ((FrameworkElement)page.FindName("AnalysisEmpty")).Visibility != Visibility.Visible)
                throw new InvalidOperationException("混用了其他磁盘的分析结果");
            page.ApplyDashboard(new HomeDashboardSnapshot { SelectedDrive = @"F:\", Drives = data.Drives,
                Analysis = new DiskSummary { Drive = @"F:\", UsedBytes = 500,
                    Categories = [new() { Name = "其他", Bytes = 250, Hex = "broken-color" }] } });
            if (((FrameworkElement)page.FindName("AnalysisEmpty")).Visibility != Visibility.Collapsed)
                throw new InvalidOperationException("有效分类未显示");
            var drives = System.IO.DriveInfo.GetDrives().Where(d => d.DriveType == System.IO.DriveType.Fixed && d.IsReady).ToList();
            if (drives.Count > 0)
            {
                var analysis = new SpaceAnalysisPage(main);
                analysis.OnNavigated(new SpaceAnalysisPage.DriveRequest(drives[^1].Name, true));
                if (((ComboBox)analysis.FindName("DriveSelector")).SelectedItem as string != drives[^1].Name ||
                    ((FrameworkElement)analysis.FindName("AnalyzePanel")).Visibility == Visibility.Visible)
                    throw new InvalidOperationException("跳转磁盘错误或自动触发了全盘扫描");
            }
        }
        finally { main.ForceClose(); }
    }

    internal static void Gauge()
    {
        var ring = new RingGauge(88, 8);
        ring.SetPercent(100);
        var arc = (System.Windows.Shapes.Path)ring.Children[1];
        if (arc.Data.Bounds.Width < 70 || arc.Data.Bounds.Height < 70) throw new InvalidOperationException("100% 圆环消失");
        var geometry = arc.Data;
        ring.SetPercent(100);
        if (!ReferenceEquals(geometry, arc.Data)) throw new InvalidOperationException("相同数值仍分配几何图形");
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++) ring.SetPercent(100);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        if (allocated > 1024) throw new InvalidOperationException("静态圆环重复刷新产生持续分配");
        ring.SetPercent(double.NaN);
        if (arc.Data != null || ring.Percent != 0) throw new InvalidOperationException("无效值未清空圆环");
        var monitor = new SystemMonitorService(); monitor.Sample(); monitor.Reset();
        if (monitor.Sample().CpuPercent != null) throw new InvalidOperationException("恢复后使用了陈旧 CPU 采样区间");
        Console.WriteLine($"[METRIC] 通用圆环相同数值更新 10000 次：{allocated} B 托管分配");
    }

    internal static void LayoutAndBoundedVisuals()
    {
        foreach (var theme in new[] { "light", "dark" })
        foreach (var (language, _) in Loc.Supported)
        {
            ThemeManager.Apply(theme); Loc.SetLanguage(language);
            var main = new MainWindow();
            try
            {
                var page = new HomePage(main);
                var data = Fixture(); page.ApplyDashboard(data);
                foreach (int width in new[] { 660, 760, 1100 })
                {
                    page.Measure(new System.Windows.Size(width, 620)); page.Arrange(new Rect(0, 0, width, 620)); page.UpdateLayout();
                    foreach (var name in new[] { "BtnScan", "BtnQuickClean", "BtnReview", "HealthBadge", "MonitorPanel", "DiskCard", "JunkSizeText", "AppsCountText", "StartupShortcut", "LargeShortcut", "AnalyzeShortcut", "HistoryShortcut" })
                    {
                        var element = (FrameworkElement)page.FindName(name);
                        var pos = element.TransformToAncestor(page).Transform(new Point());
                        if (pos.X < -1 || pos.X + element.ActualWidth > width + 1)
                            throw new InvalidOperationException($"首页越界 {theme}/{language}/{width}/{name}");
                    }
                    var monitor = (FrameworkElement)page.FindName("MonitorPanel");
                    var scan = (FrameworkElement)page.FindName("JunkSizeText");
                    if (scan.TransformToAncestor(page).Transform(new Point(scan.ActualWidth, 0)).X > monitor.TransformToAncestor(page).Transform(new Point()).X)
                        throw new InvalidOperationException("扫描摘要与系统状态重叠");
                }
                int before = VisualCount(page);
                for (int i = 0; i < 30; i++) page.ApplyDashboard(data);
                page.UpdateLayout();
                if (VisualCount(page) != before || ((Panel)page.FindName("StatsBars")).Children.Count != 0)
                    throw new InvalidOperationException("刷新导致可视树持续增长");
                if (page.MonitorRunning) throw new InvalidOperationException("离屏页面启动了采样定时器");
            }
            finally { main.ForceClose(); }
        }
    }

    internal static void CompactDetailsAndMonitor()
    {
        var main = new MainWindow();
        try
        {
            var page = new HomePage(main);
            var details = (Expander)page.FindName("DetailsExpander");
            var bars = (Panel)page.FindName("StatsBars");
            var categories = (ItemsControl)page.FindName("CategoryList");
            var suggestions = (ItemsControl)page.FindName("SuggestList");
            page.ApplyDashboard(Fixture());
            if (bars.Children.Count != 0 || categories.Items.Count != 0) throw new InvalidOperationException("折叠时创建明细");
            for (int i = 0; i < 10; i++)
            {
                details.IsExpanded = true;
                if (bars.Children.Count != 7 || categories.Items.Count != 6 || suggestions.Items.Count > 3)
                    throw new InvalidOperationException("明细数量错误");
                details.IsExpanded = false;
                page.ApplyDashboard(Fixture());
                if (bars.Children.Count != 0 || categories.Items.Count != 0 || suggestions.Items.Count != 0)
                    throw new InvalidOperationException("折叠后未释放明细");
            }
            page.OnNavigated("stats");
            if (!details.IsExpanded) throw new InvalidOperationException("统计入口未展开明细");
            var snapshot = new SystemMonitorService.Snapshot { CpuPercent = 42.2, RamUsedBytes = 4L << 30, RamTotalBytes = 8L << 30, Uptime = TimeSpan.FromMinutes(100) };
            page.ApplyMonitorSnapshot(snapshot);
            if (((ProgressBar)page.FindName("CpuBar")).Value != 42 || ((ProgressBar)page.FindName("RamBar")).Value != 50)
                throw new InvalidOperationException("监控条数值错误");
            // Warm up tiered JIT/OSR before measuring steady-state UI updates.
            for (int i = 0; i < 30000; i++) page.ApplyMonitorSnapshot(snapshot);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10000; i++) page.ApplyMonitorSnapshot(snapshot);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            if (allocated > 1024) throw new InvalidOperationException($"相同监控数据持续分配：{allocated} B");
            Console.WriteLine($"[METRIC] 首页相同监控数据更新 10000 次：{allocated} B 托管分配");
            page.ApplyMonitorSnapshot(new SystemMonitorService.Snapshot { CpuPercent = double.NaN });
            if (((TextBlock)page.FindName("CpuPercentText")).Text != "—" || ((TextBlock)page.FindName("RamPercentText")).Text != "—" ||
                ((ProgressBar)page.FindName("CpuBar")).Value != 0 || ((ProgressBar)page.FindName("RamBar")).Value != 0)
                throw new InvalidOperationException("未知监控值未清空");
        }
        finally { main.ForceClose(); }
    }

    internal static void WechatResource()
    {
        var main = new MainWindow();
        try
        {
            foreach (var theme in new[] { "light", "dark" })
            foreach (var (language, _) in Loc.Supported)
            {
                ThemeManager.Apply(theme);
                Loc.SetLanguage(language);
                var page = new SettingsPage(main);
                page.SelectSection("about");
                foreach (int width in new[] { 660, 760, 1100 })
                {
                    page.Measure(new System.Windows.Size(width, 620)); page.Arrange(new Rect(0, 0, width, 620)); page.UpdateLayout();
                    foreach (var (name, decodeWidth) in new[] { ("WechatImage", 720), ("WechatPayImage", 480) })
                    {
                        var image = (System.Windows.Controls.Image)page.FindName(name);
                        if (image.Source is not System.Windows.Media.Imaging.BitmapSource bitmap || bitmap.PixelWidth != decodeWidth || bitmap.PixelHeight < 100)
                            throw new InvalidOperationException($"微信内嵌图片解码失败或未限尺寸：{name}");
                        var position = image.TransformToAncestor(page).Transform(new Point());
                        double minWidth = name == "WechatImage" ? 200 : 140;
                        double minHeight = name == "WechatImage" ? 70 : 190;
                        if (image.ActualWidth < minWidth || image.ActualHeight < minHeight || position.X < 0 || position.X + image.ActualWidth > width)
                            throw new InvalidOperationException($"关于页图片尺寸或边界错误：{theme}/{language}/{width}/{name} {image.ActualWidth}×{image.ActualHeight}");
                    }
                    var left = (FrameworkElement)page.FindName("WechatButton");
                    var right = (FrameworkElement)page.FindName("WechatPayButton");
                    if (left.TransformToAncestor(page).Transform(new Point(left.ActualWidth, 0)).X >= right.TransformToAncestor(page).Transform(new Point()).X)
                        throw new InvalidOperationException("微信卡片重叠");
                }
                if (System.Windows.Application.Current.TryFindResource("settings.about.repo") != null)
                    throw new InvalidOperationException("已删除入口仍保留语言资源");
            }
            var previewPage = new SettingsPage(main);
            for (int i = 0; i < 4; i++)
            {
                var preview = previewPage.CreateWechatPreview(i % 2 == 0) ?? throw new InvalidOperationException("无法创建图片预览");
                if (((System.Windows.Controls.Image)preview.Content).Source is not System.Windows.Media.Imaging.BitmapSource image || image.PixelWidth != 1024 || !image.IsFrozen)
                    throw new InvalidOperationException("预览解码未限制尺寸或未冻结");
                preview.Opacity = 0;
                preview.ShowActivated = false;
                preview.Show();
                preview.UpdateLayout();
                if (((FrameworkElement)preview.Content).ActualHeight <= 0) throw new InvalidOperationException("预览窗口未完成布局");
                preview.Close();
                if (preview.Content != null) throw new InvalidOperationException("关闭预览后未释放图片引用");
            }
            main.ForceClose();
            if (previewPage.CreateWechatPreview(true) != null) throw new InvalidOperationException("已关闭主窗口仍创建预览");
        }
        finally { Loc.SetLanguage("zh-CN"); main.ForceClose(); }
    }

    private static int VisualCount(DependencyObject node)
    {
        int count = 1;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++) count += VisualCount(VisualTreeHelper.GetChild(node, i));
        return count;
    }
}
