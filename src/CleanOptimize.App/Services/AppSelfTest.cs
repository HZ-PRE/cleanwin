using System.Windows;
using System.Windows.Controls;
using CleanOptimize.App.Pages;
using CleanOptimize.Core.Models;
using CleanOptimize.Core.Rules;
using CleanOptimize.App.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Reflection;
using TreeView = System.Windows.Controls.TreeView;
using ProgressBar = System.Windows.Controls.ProgressBar;

namespace CleanOptimize.App.Services;

/// <summary>无需展示窗口的 WPF 资源和页面构造检查，不触发清理或系统设置操作。</summary>
internal static class AppSelfTest
{
    public static int RunAll()
    {
        int failed = 0, passed = 0;
        void Check(string name, Action check)
        {
            try { check(); passed++; Console.WriteLine("[PASS] " + name); }
            catch (Exception ex) { failed++; Console.WriteLine("[FAIL] " + name + ": " + ex); }
        }
        Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        Loc.SetLanguage("zh-CN");
        Check("未清理明细八语言双主题、计数、千条虚拟化和状态重置", CleanupIssueTests.FailureDetails);
        Check("空间分析长名称长路径保持回收按钮可见及详情滚动", CleanupIssueTests.LongFileActions);
        Check("更新版本、公开安装资产和请求数据边界", UpdateServiceTests.VersionsAndRequest);
        Check("更新请求合并、缓存及失败状态保留", () => RunUiTask(UpdateServiceTests.RequestLifecycle()));
        Check("更新下载校验、长度限制及取消", () => RunUiTask(UpdateServiceTests.DownloadIntegrity()));
        Check("更新双标签布局、确认文字与安装失败保护", UpdateServiceTests.UserInterface);
        Check("快捷清理确认、取消、低风险筛选与白名单保护", () => RunUiTask(CleanupFeatureTests.QuickCleanup()));
        Check("自动清理通知开关、隔离计数、失败、取消及并发保护", () => RunUiTask(CleanupFeatureTests.AutoCleanNotifications()));
        Check("卸载退出通道按完整路径隔离并兼容路径大小写", () =>
        {
            if (UninstallLifetime.EventName(@"C:\Apps\CleanOptimize.exe") != UninstallLifetime.EventName(@"c:\apps\cleanoptimize.exe") ||
                UninstallLifetime.EventName(@"C:\Apps\CleanOptimize.exe") == UninstallLifetime.EventName(@"C:\Portable\CleanOptimize.exe"))
                throw new InvalidOperationException("卸载通道未按安装路径隔离");
        });
        Check("首页评分、分类占比、零值与未扫描状态正确", HomeDashboardTests.DataStates);
        Check("紧凑首页按需明细、统计入口与监控更新分配", HomeDashboardTests.CompactDetailsAndMonitor);
        Check("关于页双微信图片、放大关闭、限尺寸解码及八语言双主题布局", HomeDashboardTests.WechatResource);
        Check("首页切换磁盘隔离分析数据且跳转不自动扫描", HomeDashboardTests.SelectedDisk);
        Check("圆环满值、无效值、重复刷新分配与 CPU 采样恢复", HomeDashboardTests.Gauge);
        Check("首页 8 语言双主题 3 宽度及重复刷新可视树有界", HomeDashboardTests.LayoutAndBoundedVisuals);
        Check("清理界面八语言区分删除与隔离量并按实际处理数显示进度", () =>
        {
            var main = new MainWindow();
            try
            {
                foreach (var (language, _) in Loc.Supported)
                {
                    Loc.SetLanguage(language);
                    var page = new SmartScanPage(main);
                    page.ApplyCleanProgress(new CleanProgress { Processed = 25, Total = 100, FreedBytes = 0, QuarantinedBytes = 4096 });
                    if (((ProgressBar)page.FindName("CleanProgressBar")).Value != 25) throw new InvalidOperationException("进度按通知数累计");
                    page.ApplyCleanOutcome(new CleanSummary { QuarantineCount = 4, QuarantinedBytes = 4096 });
                    if (((TextBlock)page.FindName("DoneFreedText")).Text != string.Format(Loc.T("done.freed.total"), Core.Utils.SizeText.OfBytes(0)) ||
                        !((TextBlock)page.FindName("DoneNoteText")).Text.Contains(string.Format(Loc.T("ui.clean.quarantined"), Core.Utils.SizeText.OfBytes(4096))))
                        throw new InvalidOperationException("隔离量被计为释放量");
                    page.ApplyCleanOutcome(new CleanSummary { Cancelled = true });
                    if (((TextBlock)page.FindName("DoneTitleText")).Text != Loc.T("done.cancelled")) throw new InvalidOperationException("取消被显示为完成");
                }
            }
            finally { main.ForceClose(); }
        });
        Check("应用搜索、清空、排序保留选择与刷新真实清单", () =>
        {
            var main = new MainWindow();
            try
            {
                var page = new AppsPage(main);
                RunUiTask(page.LoadAsync());
                var list = (VirtualRows)page.FindName("AppList");
                if (list.Items.Count == 0) throw new InvalidOperationException("无法读取应用清单");
                list.SelectedIndex = 0;
                var selected = list.SelectedItem;
                ((ComboBox)page.FindName("SortSelector")).SelectedIndex = 1;
                RunUiTask(Task.Delay(250));
                if (!ReferenceEquals(selected, list.SelectedItem)) throw new InvalidOperationException("排序丢失选择");
                var search = (System.Windows.Controls.TextBox)page.FindName("SearchBox");
                search.Text = "__no_app_" + Guid.NewGuid().ToString("N");
                RunUiTask(Task.Delay(250));
                if (list.Items.Count != 0 || ((Button)page.FindName("AppUninstall")).IsEnabled) throw new InvalidOperationException("搜索仍保留旧目标");
                ((Button)page.FindName("ClearSearch")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                RunUiTask(Task.Delay(250));
                if (search.Text.Length != 0 || list.Items.Count == 0 || ((TextBlock)page.FindName("SearchHint")).Visibility != Visibility.Visible)
                    throw new InvalidOperationException("清空搜索未恢复列表");
                var prior = EnumerationCache.GetApps();
                ((Button)page.FindName("BtnRefresh")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                RunUiTask(page.LoadAsync());
                if (ReferenceEquals(prior, EnumerationCache.GetApps()) || !((Button)page.FindName("BtnRefresh")).IsEnabled)
                    throw new InvalidOperationException("刷新未失效缓存或恢复按钮");
            }
            finally { main.ForceClose(); }
        });
        Check("应用图标本地提取、缺失回退及取消加载", () =>
        {
            var icon = AppIconService.GetAsync(Environment.ProcessPath!, 0, CancellationToken.None);
            RunUiTask(icon);
            if (ReferenceEquals(icon.Result, AppIconService.Fallback) || !icon.Result.IsFrozen) throw new InvalidOperationException("本地图标未提取或未冻结");
            var rowIcon = new AppIcon(Environment.ProcessPath!, 0);
            if (!ReferenceEquals(rowIcon.Source, icon.Result)) throw new InvalidOperationException("行图标未使用缓存");
            var missing = AppIconService.GetAsync(@"C:\__missing_cleanoptimize_icon__.ico", 0, CancellationToken.None);
            RunUiTask(missing);
            if (!ReferenceEquals(missing.Result, AppIconService.Fallback)) throw new InvalidOperationException("缺失图标无回退");
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            var task = AppIconService.GetAsync(@"C:\__cancelled_icon__.ico", 0, cancelled.Token);
            try { RunUiTask(task); throw new InvalidOperationException("取消加载未生效"); }
            catch (OperationCanceledException) { }
        });
        foreach (var theme in new[] { "light", "dark" })
        foreach (var (language, _) in Loc.Supported)
        {
            Check($"WPF 8 页面初始化及布局 {theme}/{language}", () =>
            {
                ThemeManager.Apply(theme);
                Loc.SetLanguage(language);
                var dictionaries = Application.Current.Resources.MergedDictionaries;
                if (dictionaries.Count != 3) throw new InvalidOperationException("语言字典重复保留");
                var dictionary = dictionaries[2];
                Loc.SetLanguage(language);
                if (!ReferenceEquals(dictionary, dictionaries[2])) throw new InvalidOperationException("相同语言重复分配字典");
                foreach (var key in dictionary.Keys.OfType<string>())
                    if (string.IsNullOrWhiteSpace(Loc.T(key)) || Loc.T(key) == key) throw new InvalidOperationException("缺少语言资源: " + key);
                foreach (var rule in new RuleCatalog().Rules)
                    foreach (var field in new[] { "name", "reason", "impact" })
                        if (Loc.RuleField(rule.Id, field, "__missing__") == "__missing__") throw new InvalidOperationException("缺少动态规则文案: " + rule.Id);
                if (Loc.T("privacy.offline") == "privacy.offline") throw new InvalidOperationException("缺少离线提示");
                var main = new MainWindow();
                try
                {
                    Func<Page>[] factories = [() => new HomePage(main), () => new SmartScanPage(main),
                        () => new CleanSpacePage(main), () => new SpaceAnalysisPage(main), () => new StartupPage(main),
                        () => new AppsPage(main), () => new ToolboxPage(main), () => new SettingsPage(main)];
                    foreach (var create in factories)
                    {
                        var page = create();
                        foreach (var width in new[] { 660, 760, 1100 })
                        {
                            var height = width == 660 ? 508 : 620;
                            page.Measure(new System.Windows.Size(width, height));
                            page.Arrange(new Rect(0, 0, width, height));
                            page.UpdateLayout();
                        }
                    }
                }
                finally { main.Close(); }
            });
        }
        Check("界面拒绝默认选择需确认及隐私项目", () =>
        {
            foreach (var rule in new RuleCatalog().Rules.Where(r => r.Risk >= RiskLevel.Confirm || r.Group == RuleGroup.Privacy))
            {
                var row = new ScanRowVM(new RuleResult { DefaultSelected = true, TotalSize = 1 }, rule);
                if (row.Selected) throw new InvalidOperationException(rule.Id);
            }
        });
        Check("扫描结果摘要与默认勾选数量一致", () =>
        {
            var main = new MainWindow();
            try
            {
                var page = new SmartScanPage(main);
                List<IScanRule> rules = [new UserTempRule(), new UpdateCacheRule()];
                var results = rules.Select(r => new RuleResult
                {
                    RuleId = r.Id, Name = r.Name, Group = r.Group, Risk = r.Risk, TotalSize = 5, FileCount = 1,
                }).ToList();
                foreach (var (language, _) in Loc.Supported)
                {
                    Loc.SetLanguage(language);
                    results[0].DefaultSelected = true;
                    page.BuildResultView(rules, results, refreshHealth: false);
                    AssertSummary(1, "5 B");
                    var sections = ((StackPanel)page.FindName("ResultGroups")).Children.OfType<FrameworkElement>().ToArray();
                    if (sections.Length != 2 || !LogicalDescendants(sections[1]).OfType<TextBlock>()
                        .Any(t => t.Text == Loc.T("scan.group.check")))
                        throw new InvalidOperationException("需确认项被错误归入安全清理组");
                    results[0].DefaultSelected = false;
                    page.BuildResultView(rules, results, refreshHealth: false);
                    AssertSummary(0, "0 B");
                }

                void AssertSummary(int expectedCount, string expectedSize)
                {
                    string count = ((TextBlock)page.FindName("SelCountText")).Text;
                    string size = ((TextBlock)page.FindName("SelSizeText")).Text;
                    string expected = string.Format(Loc.T("common.n.items"), expectedCount);
                    if (count != expected || size != expectedSize)
                        throw new InvalidOperationException($"摘要不匹配: {count}/{size}, 预期 {expected}/{expectedSize}");
                }
            }
            finally { main.Close(); }
        });
        Check("一万条数据仅创建可见行，滚动后释放旧行", () =>
        {
            int built = 0;
            var list = new VirtualRows { Width = 600, Height = 360 };
            list.RowFactory = item => { built++; return new TextBlock { Text = item.ToString(), Height = 36 }; };
            list.SetItems(Enumerable.Range(0, 10000).ToArray());
            Layout(list, 600, 360);
            if (built <= 0 || built > 50) throw new InvalidOperationException($"首屏创建 {built} 行");
            var initialBuilt = built;
            list.ScrollIntoView(9999);
            Layout(list, 600, 360);
            if (list.ItemContainerGenerator.ContainerFromIndex(9999) == null) throw new InvalidOperationException("无法滚动到末尾");
            if (list.ItemContainerGenerator.ContainerFromIndex(0) != null) throw new InvalidOperationException("首行容器仍被保留");
            if (built > 120) throw new InvalidOperationException($"滚动创建 {built} 行");
            Console.WriteLine($"[METRIC] 10000 条数据；首屏 {initialBuilt} 行；滚动累计 {built} 行");
            list.SetItems(null);
        });
        Check("工具按需显示、设置分组互不串页", () =>
        {
            var main = new MainWindow();
            try
            {
                var tools = new ToolboxPage(main);
                foreach (var (section, card) in new[] { ("quarantine", "QuarantineCard"), ("shred", "ShredCard"),
                    ("dup", "DuplicateCard"), ("empty", "EmptyFolderCard"), ("maint", "MaintenanceCard"),
                    ("history", "HistoryCard"), ("windows", "WindowsCard") })
                {
                    tools.SelectTool(section, refresh: false);
                    var shortcuts = (WrapPanel)tools.FindName("ToolShortcuts");
                    if (shortcuts.Visibility != Visibility.Visible || shortcuts.Children.OfType<Button>().Count() != 7)
                        throw new InvalidOperationException("工具快速切换入口缺失");
                    if (((FrameworkElement)tools.FindName(card)).Visibility != Visibility.Visible ||
                        ((FrameworkElement)tools.FindName("ToolIndex")).Visibility != Visibility.Collapsed)
                        throw new InvalidOperationException(section);
                }
                tools.SelectTool(null, refresh: false);
                if (((FrameworkElement)tools.FindName("ToolIndex")).Visibility != Visibility.Visible) throw new InvalidOperationException("工具索引未恢复");
                var settings = new SettingsPage(main);
                foreach (var section in new[] { "general", "safety", "advanced", "about" })
                {
                    settings.SelectSection(section);
                    var cards = ((StackPanel)settings.FindName("SettingsSections")).Children.OfType<Border>().ToList();
                    if (!cards.Any(c => c.Visibility == Visibility.Visible) || cards.Any(c => (c.Visibility == Visibility.Visible) != (c.Tag as string == section)))
                        throw new InvalidOperationException(section);
                }
            }
            finally { main.Close(); }
        });
        Check("大文件与应用详情跟随选择，筛选清空后不保留旧目标", () =>
        {
            var main = new MainWindow();
            try
            {
                var apps = new AppsPage(main);
                var appList = (VirtualRows)apps.FindName("AppList");
                var records = Enumerable.Range(0, 1000).Select(i => new CleanOptimize.Core.Apps.InstalledApp
                    { DisplayName = "App " + i, Publisher = "Test", EstimatedSize = 1024 }).ToArray();
                appList.SetItems(records);
                appList.SelectedIndex = 10;
                if (((TextBlock)apps.FindName("AppDetailName")).Text != "App 10") throw new InvalidOperationException("应用详情不匹配");
                appList.SetItems(Array.Empty<CleanOptimize.Core.Apps.InstalledApp>());
                if (((Button)apps.FindName("AppUninstall")).IsEnabled) throw new InvalidOperationException("空列表仍允许卸载");
                var space = new SpaceAnalysisPage(main);
                var files = (VirtualRows)space.FindName("LargeList");
                files.SetItems(new[] { new CleanOptimize.Core.Analysis.LargeFileInfo { Path = @"C:\Demo\sample.zip", Size = 1024 } });
                files.SelectedIndex = 0;
                if (((TextBlock)space.FindName("FileDetailName")).Text != "sample.zip") throw new InvalidOperationException("文件详情不匹配");
                files.SetItems(null);
                if (((StackPanel)space.FindName("FileActions")).IsEnabled) throw new InvalidOperationException("空列表仍允许删除");
            }
            finally { main.Close(); }
        });
        Check("反复切页复用页面且不累积导航历史", () =>
        {
            var main = new MainWindow();
            try
            {
                var host = (Frame)main.FindName("PageHost");
                for (int i = 0; i < 40; i++)
                {
                    var key = i % 2 == 0 ? PageKey.SmartScan : PageKey.Toolbox;
                    main.Navigate(key);
                    main.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                    if (host.CanGoBack || host.CanGoForward) throw new InvalidOperationException("导航历史仍被保留");
                }
                var cache = (Dictionary<PageKey, Page>)typeof(MainWindow).GetField("_pages", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(main)!;
                if (cache.Count != 2 || !ReferenceEquals(host.Content, cache[PageKey.Toolbox]))
                    throw new InvalidOperationException("页面缓存不正确");
                Console.WriteLine("[METRIC] 40 次切页；2 个缓存页面；无前进/后退历史");
            }
            finally { main.Close(); }
        });
        Check("语言选项显示原生名称，保留两项禁用上报设置，移除无效通知", () =>
        {
            var main = new MainWindow();
            try
            {
                var settings = new SettingsPage(main);
                var general = (StackPanel)settings.FindName("GeneralSection");
                var combos = LogicalDescendants(general).OfType<ComboBox>();
                var languages = combos.Single(c => c.Items.Count == Loc.Supported.Length + 1);
                for (int i = 0; i < Loc.Supported.Length; i++)
                    if ((string)languages.Items[i + 1] != Loc.Supported[i].NativeName)
                        throw new InvalidOperationException("语言名称与代码映射不正确");
                var disabled = LogicalDescendants(settings).OfType<System.Windows.Controls.Primitives.ToggleButton>()
                    .Where(t => !t.IsEnabled).ToArray();
                if (disabled.Length != 2 || disabled.Any(t => t.IsChecked != false)) throw new InvalidOperationException("隐私开关丢失或仍有其他无效开关");
                var notifications = (StackPanel)settings.FindName("NotifySection");
                if (LogicalDescendants(notifications).OfType<System.Windows.Controls.Primitives.ToggleButton>().Count() != 2)
                    throw new InvalidOperationException("仍有无效通知选项");
            }
            finally { main.Close(); }
        });
        Check("重复文件勾选状态在虚拟行回收后保留", () =>
        {
            var main = new MainWindow();
            try
            {
                var page = new ToolboxPage(main);
                var group = new Core.Analysis.DuplicateGroup { Size = 1024 };
                group.Files.AddRange(Enumerable.Range(0, 1000).Select(i => new Core.Analysis.DuplicateFile
                    { Path = @"C:\Demo\" + i + ".txt", Size = 1024 }));
                var card = (Border)typeof(ToolboxPage).GetMethod("BuildDupGroup", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(page, [group])!;
                var list = ((StackPanel)card.Child).Children.OfType<VirtualRows>().Single();
                Layout(list, 600, 320);
                System.Windows.Controls.CheckBox Checkbox(int index)
                {
                    var container = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(index)!;
                    return ((StackPanel)container.Content).Children.OfType<System.Windows.Controls.CheckBox>().Single();
                }
                Checkbox(0).IsChecked = true;
                if (!group.Files[0].Selected) throw new InvalidOperationException("勾选未写回数据");
                list.ScrollIntoView(group.Files[^1]); Layout(list, 600, 320);
                if (list.ItemContainerGenerator.ContainerFromIndex(0) != null) throw new InvalidOperationException("旧行未回收");
                list.ScrollIntoView(group.Files[0]); Layout(list, 600, 320);
                if (Checkbox(0).IsChecked != true || group.Files[^1].Selected) throw new InvalidOperationException("勾选丢失或串行");
            }
            finally { main.Close(); }
        });
        Check("重复文件回收按钮在最小页面宽度及各语言下可见", () =>
        {
            foreach (var (code, _) in Loc.Supported)
            {
                Loc.SetLanguage(code);
                var main = new MainWindow();
                try
                {
                    var page = new ToolboxPage(main);
                    page.SelectTool("dup", refresh: false);
                    var result = new Core.Analysis.DuplicateScanResult();
                    var group = new Core.Analysis.DuplicateGroup { Size = 4096 };
                    group.Files.Add(new Core.Analysis.DuplicateFile { Path = @"C:\Demo\keep.bin", Size = 4096 });
                    group.Files.Add(new Core.Analysis.DuplicateFile { Path = @"C:\Demo\selected.bin", Size = 4096, Selected = true });
                    result.Groups.Add(group);
                    typeof(ToolboxPage).GetField("_dupResult", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(page, result);
                    typeof(ToolboxPage).GetMethod("RenderDup", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(page, null);
                    foreach (var (width, height) in new[] { (660, 508), (760, 588) })
                    {
                        Layout(page, width, height);
                        var button = (Button)page.FindName("BtnDupClean");
                        var bounds = button.TransformToAncestor(page).TransformBounds(new Rect(button.RenderSize));
                        if (button.Visibility != Visibility.Visible || bounds.Left < 0 || bounds.Right > width || bounds.Bottom > height)
                            throw new InvalidOperationException($"回收按钮被裁切: {code}/{bounds}");
                    }
                }
                finally { main.Close(); }
            }
        });
        Check("窗口隐藏恢复、关闭后回调及真实进程退出", WindowLifetimeTests.VerifyInChildProcess);
        Check("关闭前排队的托盘回调不会复活窗口", () =>
        {
            var main = new MainWindow();
            using var tray = new NotifyIconService(main);
            bool ran = false;
            main.Dispatcher.BeginInvoke(() =>
            {
                if (tray.OpenMainWindow() || main.RestoreFromTray()) throw new InvalidOperationException("迟到回调恢复了窗口");
                main.ShowToast("late callback");
                ran = true;
            });
            main.ForceClose();
            main.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            if (!ran || !main.IsClosed) throw new InvalidOperationException("未验证排队回调");
        });
        Check("空目录实际重扫、取消与结果行渲染（8 语言/2 主题）", () =>
        {
            var root = System.IO.Path.Combine(Core.Store.SettingsService.DataDir, "empty-ui");
            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(root, "leaf"));
            using var recycledFixture = new Core.SelfTest.RecycleTestScope();
            recycledFixture.Track(System.IO.Path.Combine(root, "leaf"));
            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(root, "tree", "child"));
            System.IO.File.WriteAllText(System.IO.Path.Combine(root, "keep.txt"), "keep");
            var main = new MainWindow();
            try
            {
                var page = new ToolboxPage(main);
                page.SelectTool("empty", refresh: false);
                foreach (var theme in new[] { "light", "dark" })
                foreach (var (code, _) in Loc.Supported)
                {
                    ThemeManager.Apply(theme); Loc.SetLanguage(code);
                    RunUiTask(page.ScanEmptyAsync(new[] { root }));
                    Layout(page, 760, 620);
                    var list = (VirtualRows)page.FindName("EmptyList");
                    if (list.Items.Count != 2 || list.ItemContainerGenerator.ContainerFromIndex(0) == null ||
                        list.ItemContainerGenerator.ContainerFromIndex(1) == null)
                        throw new InvalidOperationException("未渲染有/无子目录两类结果行");
                }
                var cancelled = page.ScanEmptyAsync(new[] { root });
                page.TryCancelCurrent(); RunUiTask(cancelled);
                RunUiTask(page.ScanEmptyAsync(new[] { root }));
                if (!((Button)page.FindName("BtnEmptyScan")).IsEnabled) throw new InvalidOperationException("重扫按钮未恢复");
                var leaf = System.IO.Path.Combine(root, "leaf");
                RunUiTask(page.CleanEmptyAsync(new[] { leaf }));
                if (System.IO.Directory.Exists(leaf) || ((VirtualRows)page.FindName("EmptyList")).Items.Count != 1)
                    throw new InvalidOperationException("空目录回收结果未同步到界面");
                RunUiTask(page.ScanEmptyAsync(new[] { root }));
                Layout(page, 760, 620);
            }
            finally { main.ForceClose(); Core.Utils.FileUtil.DeleteDirectoryTree(root); }
        });
        Check("隔离区界面清空只读文件、显示占用失败并可重试", () =>
        {
            var quarantine = Core.Rules.QuarantineService.Instance;
            if (quarantine.Entries.Count != 0) throw new InvalidOperationException("隔离测试需要空的隔离测试目录");
            var root = Core.Store.SettingsService.DataDir;
            var normal = System.IO.Path.Combine(root, "quarantine-normal.txt");
            var readOnly = System.IO.Path.Combine(root, "quarantine-readonly.txt");
            var locked = System.IO.Path.Combine(root, "quarantine-locked.txt");
            foreach (var path in new[] { normal, readOnly, locked }) System.IO.File.WriteAllText(path, "owned sample");
            System.IO.File.SetAttributes(readOnly, System.IO.FileAttributes.ReadOnly);
            foreach (var path in new[] { normal, readOnly, locked })
                if (!quarantine.MoveIntoQuarantine(path, "ui-test", out var error)) throw new InvalidOperationException(error);
            var lockedEntry = quarantine.Entries.Single(e => e.OriginalPath == locked);
            var stored = System.IO.Path.Combine(Core.Store.SettingsService.QuarantineDir, lockedEntry.StoredName);
            var main = new MainWindow();
            try
            {
                var page = new ToolboxPage(main);
                page.SelectTool("quarantine");
                using (var handle = new System.IO.FileStream(stored, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.None))
                {
                    RunUiTask(page.PurgeQuarantineAsync());
                    Layout(page, 760, 620);
                    if (quarantine.Entries.Count != 1 || !((Button)page.FindName("BtnPurgeQuarantine")).IsEnabled ||
                        !((TextBlock)page.FindName("QuarantineOutcomeText")).Text.StartsWith(string.Format(Loc.T("ui.operation.result"), 2, 1)))
                        throw new InvalidOperationException("只读文件未删除或占用失败未保留/报告");
                }
                RunUiTask(page.PurgeQuarantineAsync());
                if (quarantine.Entries.Count != 0 || ((Button)page.FindName("BtnPurgeQuarantine")).IsEnabled ||
                    ((VirtualRows)page.FindName("QuarantineList")).Items.Count != 0)
                    throw new InvalidOperationException("解锁后不能清空隔离区");
            }
            finally { main.ForceClose(); }
        });
        Check("实际页面离屏渲染", () => CapturePages());
        Console.WriteLine($"== WPF 检查: {passed} 通过, {failed} 失败（页面构造/布局，不等同于人工交互验收） ==");
        return failed == 0 ? 0 : 1;
    }

    internal static void RunUiTask(Task task)
    {
        if (!task.IsCompleted)
        {
            var dispatcher = Application.Current.Dispatcher;
            var frame = new System.Windows.Threading.DispatcherFrame();
            var timeout = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            timeout.Tick += (_, _) => frame.Continue = false;
            timeout.Start();
            task.ContinueWith(_ => dispatcher.BeginInvoke(() => frame.Continue = false), TaskScheduler.Default);
            try { System.Windows.Threading.Dispatcher.PushFrame(frame); }
            finally { timeout.Stop(); }
        }
        if (!task.IsCompleted) throw new TimeoutException("异步界面检查超时");
        task.GetAwaiter().GetResult();
    }

    private static IEnumerable<DependencyObject> LogicalDescendants(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var nested in LogicalDescendants(child)) yield return nested;
        }
    }

    private static void Layout(FrameworkElement element, double width, double height)
    {
        element.Measure(new System.Windows.Size(width, height));
        element.Arrange(new Rect(0, 0, width, height));
        element.UpdateLayout();
    }

    private static void CapturePages()
    {
        var output = Environment.GetEnvironmentVariable("CM_UI_SNAPSHOT_DIR");
        if (string.IsNullOrWhiteSpace(output)) return;
        if (!Core.Utils.PathUtil.IsLocalPath(output) || Core.Utils.PathUtil.HasReparseAncestor(output)) throw new InvalidOperationException("截图目录无效");
        System.IO.Directory.CreateDirectory(output);
        Loc.SetLanguage("zh-CN");
        foreach (var theme in new[] { "light", "dark" })
        {
            ThemeManager.Apply(theme);
            var main = new MainWindow();
            try
            {
                var scan = new SmartScanPage(main);
                var home = new HomePage(main);
                home.ApplyDashboard(HomeDashboardTests.Fixture());
                home.UpdateMonitor();
                RunUiTask(Task.Delay(100));
                home.UpdateMonitor();
                var rules = new List<IScanRule> { new UserTempRule(), new UpdateCacheRule() };
                scan.BuildResultView(rules, rules.Select(r => new RuleResult { RuleId = r.Id, Name = r.Name, Group = r.Group,
                    Risk = r.Risk, FileCount = 128, TotalSize = 128L * 1024 * 1024 }).ToList(), refreshHealth: false);
                ((FrameworkElement)scan.FindName("PanelIdle")).Visibility = Visibility.Collapsed;
                ((FrameworkElement)scan.FindName("PanelResults")).Visibility = Visibility.Visible;
                var space = new SpaceAnalysisPage(main);
                var tree = (TreeView)space.FindName("FolderTree");
                foreach (var (name, size) in new[] { ("Users", 76L), ("Windows", 42L), ("Program Files", 38L) })
                    tree.Items.Add(new FolderNodeVM(name, size * 1024 * 1024 * 1024, @"C:\" + name));
                ((FrameworkElement)space.FindName("TreeCard")).Visibility = Visibility.Visible;
                ((TextBlock)space.FindName("OverviewUsedText")).Text = "186 GB / 256 GB";
                ((ProgressBar)space.FindName("DiskUsageBar")).Value = 73;
                ((FrameworkElement)space.FindName("OverviewEmptyText")).Visibility = Visibility.Collapsed;
                var apps = new AppsPage(main);
                var appList = (VirtualRows)apps.FindName("AppList");
                var examples = EnumerationCache.GetApps().Where(a => !string.IsNullOrEmpty(a.IconPath)).Take(8).ToList();
                foreach (var example in examples) RunUiTask(AppIconService.GetAsync(example.IconPath, example.IconIndex, CancellationToken.None));
                appList.SetItems(examples);
                ((TextBlock)apps.FindName("AppCount")).Text = string.Format(Loc.T("ui.apps.count"), examples.Count, examples.Count);
                appList.SelectedIndex = 0;
                main.ApplyUpdateResult(UpdateServiceTests.NewRelease);
                var updateDialog = new Windows.UpdateWindow(main, UpdateServiceTests.NewRelease);
                try
                {
                    var updateContent = (FrameworkElement)updateDialog.Content;
                    updateContent.Measure(new System.Windows.Size(500, double.PositiveInfinity));
                    int updateHeight = (int)Math.Ceiling(updateContent.DesiredSize.Height);
                    Layout(updateContent, 500, updateHeight);
                    var updateBitmap = new RenderTargetBitmap(500, updateHeight, 96, 96, PixelFormats.Pbgra32);
                    updateBitmap.Render(updateContent);
                    var updateEncoder = new PngBitmapEncoder(); updateEncoder.Frames.Add(BitmapFrame.Create(updateBitmap));
                    using var updateFile = System.IO.File.Create(System.IO.Path.Combine(output, $"update-confirm-{theme}.png"));
                    updateEncoder.Save(updateFile);
                }
                finally { updateDialog.Close(); }
                var about = new SettingsPage(main); about.SelectSection("about");
                var expandedHome = new HomePage(main); expandedHome.ApplyDashboard(HomeDashboardTests.Fixture());
                ((Expander)expandedHome.FindName("DetailsExpander")).IsExpanded = true;
                foreach (var (name, page) in new (string, Page)[] { ("about", about), ("home-details", expandedHome), ("home", home), ("scan", scan), ("space", space),
                    ("apps", apps), ("startup", new StartupPage(main)), ("tools", new ToolboxPage(main)), ("settings", new SettingsPage(main)) })
                {
                    page.Background = (Brush)Application.Current.FindResource("PageBgBrush");
                    Layout(page, 760, 620);
                    if (name == "about") { ((ScrollViewer)page.FindName("SettingsScroll")).ScrollToEnd(); page.UpdateLayout(); }
                    var bitmap = new RenderTargetBitmap(760, 620, 96, 96, PixelFormats.Pbgra32);
                    bitmap.Render(page);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var file = System.IO.File.Create(System.IO.Path.Combine(output, $"{name}-{theme}.png"));
                    encoder.Save(file);
                }
                var toolPage = new ToolboxPage(main); toolPage.SelectTool("empty", refresh: false);
                toolPage.Background = (Brush)Application.Current.FindResource("PageBgBrush");
                Layout(toolPage, 760, 620);
                var toolBitmap = new RenderTargetBitmap(760, 620, 96, 96, PixelFormats.Pbgra32); toolBitmap.Render(toolPage);
                var toolEncoder = new PngBitmapEncoder(); toolEncoder.Frames.Add(BitmapFrame.Create(toolBitmap));
                using (var toolFile = System.IO.File.Create(System.IO.Path.Combine(output, $"tools-active-{theme}.png"))) toolEncoder.Save(toolFile);
                var shell = (FrameworkElement)main.Content;
                main.Navigate(PageKey.Home);
                main.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                var dashboard = (HomePage)main.PageHost.Content;
                RunUiTask(dashboard.RefreshAsync());
                dashboard.UpdateMonitor();
                foreach (var size in new[] { (1000, 680), (900, 600) })
                {
                    Layout(shell, size.Item1, size.Item2);
                    var shot = new RenderTargetBitmap(size.Item1, size.Item2, 96, 96, PixelFormats.Pbgra32); shot.Render(shell);
                    var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(shot));
                    using var file = System.IO.File.Create(System.IO.Path.Combine(output, $"home-shell-{size.Item1}-{theme}.png")); png.Save(file);
                }
                main.Navigate(PageKey.SmartScan);
                main.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                Layout(shell, 1000, 680);
                var shellBitmap = new RenderTargetBitmap(1000, 680, 96, 96, PixelFormats.Pbgra32);
                shellBitmap.Render(shell);
                var shellEncoder = new PngBitmapEncoder(); shellEncoder.Frames.Add(BitmapFrame.Create(shellBitmap));
                using var shellFile = System.IO.File.Create(System.IO.Path.Combine(output, $"shell-{theme}.png"));
                shellEncoder.Save(shellFile);
                main.Navigate(PageKey.Apps);
                main.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                if (main.PageHost.Content is not AppsPage compactApps) throw new InvalidOperationException("小窗口截图导航未完成");
                RunUiTask(compactApps.LoadAsync());
                Layout(shell, 900, 600);
                var compactBitmap = new RenderTargetBitmap(900, 600, 96, 96, PixelFormats.Pbgra32);
                compactBitmap.Render(shell);
                var compactEncoder = new PngBitmapEncoder(); compactEncoder.Frames.Add(BitmapFrame.Create(compactBitmap));
                using var compactFile = System.IO.File.Create(System.IO.Path.Combine(output, $"shell-compact-{theme}.png"));
                compactEncoder.Save(compactFile);
            }
            finally { main.Close(); }
        }
    }
}
