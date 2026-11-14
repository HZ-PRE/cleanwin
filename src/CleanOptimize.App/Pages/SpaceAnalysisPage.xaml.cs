using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CleanOptimize.App.Services;
using CleanOptimize.Core.Analysis;
using CleanOptimize.Core.Store;
using CleanOptimize.Core.Utils;

namespace CleanOptimize.App.Pages;

public partial class SpaceAnalysisPage : Page, IParamPage
{
    internal sealed record DriveRequest(string Drive, bool Large);
    private readonly MainWindow _main;
    private readonly SpaceAnalysisService _analyzer = new();
    private CancellationTokenSource? _cts;
    internal bool OperationActive => _cts != null;
    private DriveAnalysis? _lastAnalysis;
    private bool _onLargeTab;

    public SpaceAnalysisPage(MainWindow main)
    {
        InitializeComponent();
        _main = main;
        LargeList.RowFactory = item => BuildLargeRow((LargeFileInfo)item);
        FolderTree.AddHandler(TreeViewItem.ExpandedEvent, new RoutedEventHandler(FolderTree_Expanded));
        Loaded += (s, e) =>
        {
            if (DriveSelector.Items.Count == 0)
            {
                foreach (var d in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady))
                    DriveSelector.Items.Add(d.Name);
                if (DriveSelector.Items.Count > 0)
                    DriveSelector.SelectedIndex = 0;
            }
        };
    }

    public async void OnNavigated(object? param)
    {
        if (param is DriveRequest request)
        {
            if (_cts != null) return;
            if (DriveSelector.Items.Count == 0)
                foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady))
                    DriveSelector.Items.Add(drive.Name);
            if (DriveSelector.Items.Contains(request.Drive)) DriveSelector.SelectedItem = request.Drive;
            else if (DriveSelector.Items.Count > 0 && DriveSelector.SelectedIndex < 0) DriveSelector.SelectedIndex = 0;
            if (_lastAnalysis != null && !string.Equals(_lastAnalysis.DriveName, CurrentDrive, StringComparison.OrdinalIgnoreCase))
            {
                _lastAnalysis = null;
                FolderTree.Items.Clear();
                CategoryRow.Children.Clear();
                OverviewDriveText.Text = CurrentDrive;
                OverviewUsedText.Text = "—";
                OverviewTimeText.Text = "";
                DiskUsageBar.Value = 0;
                OverviewEmptyText.Visibility = Visibility.Visible;
            }
            // Navigation selects the drive; only the explicit Analyze button starts a full scan.
            if (request.Large) TabLarge.IsChecked = true;
            else TabOverview.IsChecked = true;
            _onLargeTab = request.Large;
            LargeCard.Visibility = request.Large ? Visibility.Visible : Visibility.Collapsed;
            TreeCard.Visibility = !request.Large && _lastAnalysis != null ? Visibility.Visible : Visibility.Collapsed;
            RenderLargeFiles();
            UpdateAnalyzePanel();
            return;
        }
        if (param as string == "large")
        {
            TabLarge.IsChecked = true;
        }
        else if (param as string == "analyze")
        {
            TabOverview.IsChecked = true;
            await RunAnalyzeAsync();
        }
        else if (param as string == "analyze-large")
        {
            _switchToLargeAfterAnalyze = true;
            await RunAnalyzeAsync();
        }
    }

    private bool _switchToLargeAfterAnalyze;

    private async Task RunAnalyzeAsync()
    {
        try { await AnalyzeAsync(); }
        catch (Exception ex) { Services.AppLog.Error("OnNavigated.analyze", ex); }
        if (_switchToLargeAfterAnalyze)
        {
            _switchToLargeAfterAnalyze = false;
            TabLarge.IsChecked = true;
        }
    }

    private string CurrentDrive => DriveSelector.SelectedItem as string ?? "C:\\";

    // ————— 标签切换 —————

    private void OverviewTab_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        _onLargeTab = false;
        LargeCard.Visibility = Visibility.Collapsed;
        TreeCard.Visibility = _lastAnalysis != null ? Visibility.Visible : Visibility.Collapsed;
        UpdateAnalyzePanel();
    }

    private void LargeTab_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        _onLargeTab = true;
        TreeCard.Visibility = Visibility.Collapsed;
        LargeCard.Visibility = Visibility.Visible;
        UpdateAnalyzePanel();
        RenderLargeFiles();
    }

    private void UpdateAnalyzePanel()
    {
        AnalyzePanel.Visibility = _cts != null ? Visibility.Visible : Visibility.Collapsed;
        if (_cts == null)
        {
            OverviewCard.Visibility = Visibility.Visible;
        }
    }

    // ————— 分析 —————

    private async void BtnAnalyze_Click(object sender, RoutedEventArgs e) => await AnalyzeAsync();

    private async Task AnalyzeAsync()
    {
        if (_cts != null) return;
        var drive = CurrentDrive;
        _cts = new CancellationTokenSource();
        AnalyzePanel.Visibility = Visibility.Visible;
        OverviewCard.Visibility = Visibility.Collapsed;
        TreeCard.Visibility = Visibility.Collapsed;
        LargeCard.Visibility = _onLargeTab ? Visibility.Visible : Visibility.Collapsed;
        BtnAnalyze.IsEnabled = false;
        DriveSelector.IsEnabled = false;
        AnalyzeFilesText.Text = string.Format(Services.Loc.T("space.scanned"), 0);
        AnalyzeBytesText.Text = string.Format(Services.Loc.T("space.total"), "0 B");

        var threshold = 500L * 1024 * 1024;
        var progress = new Progress<SpaceAnalysisService.ProgressInfo>(p =>
        {
            AnalyzeDirText.Text = p.CurrentDir;
            AnalyzeFilesText.Text = string.Format(Services.Loc.T("space.scanned"), p.ScannedFiles);
            AnalyzeBytesText.Text = string.Format(Services.Loc.T("space.total"), SizeText.OfBytes(p.ScannedBytes));
        });

        DriveAnalysis analysis;
        try
        {
            analysis = await _analyzer.AnalyzeAsync(drive, threshold, _cts.Token, progress);
        }
        catch (OperationCanceledException)
        {
            FinishAnalyzeUi();
            _main.ShowToast(Services.Loc.T("space.cancelled"), ToastType.Info);
            return;
        }
        catch (Exception ex)
        {
            FinishAnalyzeUi();
            Services.AppLog.Error("AnalyzeAsync", ex);
            _main.ShowToast(Services.Loc.T("space.failed") + ex.Message, ToastType.Warning);
            return;
        }
        FinishAnalyzeUi();
        _lastAnalysis = analysis;
        // 渲染与保存必须互不影响，异常记录日志
        try
        {
            RenderOverview(analysis);
            RenderTree(analysis);
            RenderLargeFiles();
            _main.ShowToast(string.Format(Services.Loc.T("space.done"), analysis.Duration.TotalSeconds));
        }
        catch (Exception ex)
        {
            Services.AppLog.Error("RenderAnalysis", ex);
            _main.ShowToast(Services.Loc.T("space.rendererror"), ToastType.Warning);
        }
        try
        {
            DiskCache.Save(new DiskSummary
            {
                Drive = drive,
                TotalBytes = analysis.TotalBytes,
                UsedBytes = analysis.UsedBytes,
                FreeBytes = analysis.FreeBytes,
                LargeFileCount = analysis.LargeFiles.Count,
                Time = DateTime.Now,
                Categories = analysis.Categories.Take(6).Select(c => new DiskSummary.CatItem
                {
                    Name = CategoryText.Of(c.Category),
                    Bytes = c.Bytes,
                    Percent = c.Percent,
                    Hex = CategoryText.HexOf(c.Category),
                }).ToList(),
            });
        }
        catch (Exception ex)
        {
            Services.AppLog.Error("SaveDiskSummary", ex);
        }
    }

    private void FinishAnalyzeUi()
    {
        _cts?.Dispose();
        _cts = null;
        DriveSelector.IsEnabled = true;
        AnalyzePanel.Visibility = Visibility.Collapsed;
        OverviewCard.Visibility = Visibility.Visible;
        BtnAnalyze.IsEnabled = true;
    }

    private void BtnCancelAnalyze_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();
    public void TryCancelCurrent() => _cts?.Cancel();

    private void RenderOverview(DriveAnalysis a)
    {
        OverviewDriveText.Text = a.DriveName;
        OverviewUsedText.Text = string.Format(Services.Loc.T("space.used.f"), SizeText.OfBytes(a.UsedBytes), SizeText.OfBytes(a.TotalBytes), a.UsedPercent, SizeText.OfBytes(a.FreeBytes));
        OverviewTimeText.Text = string.Format(Services.Loc.T("space.analyzed.f"), DateTime.Now.ToString("HH:mm"), a.ScannedFiles, a.Duration.TotalSeconds);
        DiskUsageBar.Value = Math.Clamp(a.UsedPercent, 0, 100);
        CategoryRow.Children.Clear();
        foreach (var c in a.Categories.Take(6))
        {
            var chip = new Border
            {
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(14, 8, 14, 8),
                Margin = new Thickness(0, 0, 10, 6),
                Background = (Brush)FindResource("SidebarBgBrush"),
                MinWidth = 110,
            };
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock
            {
                Text = $"{CategoryText.Of(c.Category)}  {c.Percent:F1}%",
                FontSize = 11.5,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)FindResource("TextSecondaryBrush"),
            });
            sp.Children.Add(new TextBlock
            {
                Text = SizeText.OfBytes(c.Bytes, 0),
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)FindResource("TextSecondaryBrush"),
                Margin = new Thickness(0, 2, 0, 0),
            });
            chip.Child = sp;
            CategoryRow.Children.Add(chip);
        }
        OverviewEmptyText.Visibility = Visibility.Collapsed;
        TreeCard.Visibility = _onLargeTab ? Visibility.Collapsed : Visibility.Visible;
    }

    // ————— 目录树 —————

    private void RenderTree(DriveAnalysis a)
    {
        FolderTree.Items.Clear();
        var root = new FolderNodeVM(a.DriveName, a.Root.Size, a.DriveName);
        foreach (var child in a.Root.Children)
            root.Children.Add(MakeNode(child));
        FolderTree.Items.Add(root);
    }

    private FolderNodeVM MakeNode(Core.Analysis.FolderNode node)
    {
        var vm = new FolderNodeVM(node.Name, node.Size, node.FullPath);
        // 预置占位子节点以显示展开箭头，展开时再真实加载
        if (node.Size > 1024 * 1024)
            vm.Children.Add(new FolderNodeVM("…", 0, ""));
        return vm;
    }

    private void FolderTree_Expanded(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not TreeViewItem { Header: FolderNodeVM vm }) return;
        if (_lastAnalysis == null) return;
        if (vm.Children.Count != 1 || vm.Children[0].FullPath != "") return; // 已加载
        vm.Children.Clear();
        foreach (var c in _lastAnalysis.GetChildren(vm.FullPath))
        {
            var child = MakeNode(c);
            vm.Children.Add(child);
        }
        if (vm.Children.Count == 0)
            vm.Children.Add(new FolderNodeVM(Services.Loc.T("space.nosubdir"), 0, "done"));
    }

    // ————— 大文件 —————

    private void LargeThreshold_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded) RenderLargeFiles();
    }

    // ————— 大文件类型筛选 —————

    private string _typeFilter = "";   // "" = 全部,否则 Core 分类中文名

    private void BuildTypeFilter()
    {
        if (TypeFilterHost.Children.Count > 0) return;
        var cats = new[] { "", "视频", "图片", "音频", "文档", "压缩包", "其他" };
        foreach (var c in cats)
        {
            var rb = new RadioButton
            {
                Style = (Style)FindResource("PillTab"),
                GroupName = "largeType",
                Tag = c,
                IsChecked = c == "",
                Content = c == "" ? Services.Loc.T("space.filter.all") : Services.Loc.CategoryDisplay(c),
                FontSize = 12,
                Padding = new Thickness(13, 5, 13, 6),
            };
            rb.Checked += (s, e) => { _typeFilter = c; RenderLargeFiles(); };
            TypeFilterHost.Children.Add(rb);
        }
    }

    private void DriveSelector_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        _lastAnalysis = null;
        _typeFilter = "";
        foreach (var rb in TypeFilterHost.Children.OfType<RadioButton>().ToList())
            if ((string?)rb.Tag == "") rb.IsChecked = true;
        FolderTree.Items.Clear();
        TreeCard.Visibility = Visibility.Collapsed;
        LargeList.SetItems(null);
        CategoryRow.Children.Clear();
        OverviewDriveText.Text = CurrentDrive;
        OverviewTimeText.Text = "";
        var cached = DiskCache.Load(CurrentDrive);
        if (cached != null)
        {
            OverviewUsedText.Text = string.Format(Services.Loc.T("space.used.f2"), SizeText.OfBytes(cached.UsedBytes), SizeText.OfBytes(cached.TotalBytes), SizeText.OfBytes(cached.FreeBytes));
            OverviewTimeText.Text = string.Format(Services.Loc.T("space.lastanalysis"), cached.Time.ToString("yyyy-MM-dd HH:mm"));
            DiskUsageBar.Value = cached.TotalBytes > 0
                ? Math.Clamp(cached.UsedBytes * 100.0 / cached.TotalBytes, 0, 100)
                : 0;
            foreach (var c in cached.Categories)
            {
                var chip = new Border
                {
                    CornerRadius = new CornerRadius(9),
                    Padding = new Thickness(14, 8, 14, 8),
                    Margin = new Thickness(0, 0, 10, 6),
                    Background = (Brush)FindResource("SidebarBgBrush"),
                    MinWidth = 110,
                };
                var sp = new StackPanel();
                sp.Children.Add(new TextBlock
                {
                    Text = $"{c.Name}  {c.Percent:F1}%",
                    FontSize = 11.5,
                    FontWeight = FontWeights.Bold,
                    Foreground = (Brush)FindResource("TextSecondaryBrush"),
                });
                sp.Children.Add(new TextBlock
                {
                    Text = SizeText.OfBytes(c.Bytes, 0),
                    FontSize = 13,
                    FontWeight = FontWeights.Bold,
                    Foreground = (Brush)FindResource("TextSecondaryBrush"),
                    Margin = new Thickness(0, 2, 0, 0),
                });
                chip.Child = sp;
                CategoryRow.Children.Add(chip);
            }
            OverviewEmptyText.Visibility = Visibility.Collapsed;
        }
        else
        {
            OverviewUsedText.Text = Services.Loc.T("space.notanalyzed");
            OverviewEmptyText.Visibility = Visibility.Visible;
        }
        RenderLargeFiles();
    }

    private void RenderLargeFiles()
    {
        LargeList.SetItems(null);
        BuildTypeFilter();
        double mb = 500;
        if (LargeThreshold.SelectedItem is ComboBoxItem it && it.Tag is string tag)
            mb = double.Parse(tag);
        var threshold = (long)(mb * 1024 * 1024);

        var disk = DiskCache.Load(CurrentDrive);
        OverviewDriveText.Text = disk?.Drive ?? CurrentDrive;
        var files = _lastAnalysis?.LargeFiles
            ?? Enumerable.Empty<LargeFileInfo>();

        // 类型筛选(按 Core 分类)
        if (_typeFilter.Length > 0)
            files = files.Where(f => CategoryText.Of(SpaceAnalysisService.ClassifyFile(f.Path)) == _typeFilter);

        var list = files.Where(f => f.Size >= threshold).OrderByDescending(f => f.Size).Take(1000).ToList();
        LargeEmptyText.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_lastAnalysis == null && disk == null)
            LargeEmptyText.Text = Services.Loc.T("space.empty");
        else if (list.Count == 0)
            LargeEmptyText.Text = string.Format(Services.Loc.T("space.large.none"), mb);

        long shownBytes = list.Sum(f => f.Size);
        LargeSummaryText.Text = list.Count > 0
            ? string.Format(Services.Loc.T("space.large.summary"), list.Count, SizeText.OfBytes(shownBytes))
            : "";

        LargeList.SetItems(list);
    }

    private Border BuildLargeRow(LargeFileInfo f)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel { Margin = new Thickness(0, 0, 10, 0) };
        text.Children.Add(new TextBlock { Text = Path.GetFileName(f.Path), FontSize = 13,
            TextTrimming = TextTrimming.CharacterEllipsis, Foreground = (Brush)FindResource("TextPrimaryBrush") });
        text.Children.Add(new TextBlock { Text = f.Path, FontSize = 11, Margin = new Thickness(0, 4, 0, 0),
            ToolTip = f.Path, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = (Brush)FindResource("TextSecondaryBrush") });
        var size = new TextBlock { Text = SizeText.OfBytes(f.Size), VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)FindResource("TextPrimaryBrush") };
        Grid.SetColumn(size, 1); grid.Children.Add(text); grid.Children.Add(size);
        var row = new Border { Padding = new Thickness(12, 14, 12, 14), Child = grid };
        Controls.UiPathMenu.Attach(row, f.Path);
        return row;
    }

    private void LargeList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var file = LargeList.SelectedItem as LargeFileInfo;
        FileActions.IsEnabled = file != null;
        FileDetailName.Text = file == null ? Loc.T("ui.select.file") : Path.GetFileName(file.Path);
        FileDetailMeta.Text = file == null ? "" : $"{SizeText.OfBytes(file.Size)}\n\n{file.ModifiedTime:yyyy-MM-dd HH:mm}\n\n{file.Path}";
    }
    private void FileOpen_Click(object sender, RoutedEventArgs e)
    {
        if (LargeList.SelectedItem is LargeFileInfo file)
            try { ShellUtil.ShowInExplorer(file.Path); } catch (Exception ex) { _main.ShowToast(ex.Message, ToastType.Warning); }
    }
    private void FileWhitelist_Click(object sender, RoutedEventArgs e)
    {
        if (LargeList.SelectedItem is not LargeFileInfo file) return;
        if (!SettingsService.Current.Whitelist.Contains(file.Path, StringComparer.OrdinalIgnoreCase))
        { SettingsService.Current.Whitelist.Add(file.Path); SettingsService.Save(); }
        _main.ShowToast(Loc.T("toast.whitelisted"));
    }
    private void FileRecycle_Click(object sender, RoutedEventArgs e)
    {
        if (LargeList.SelectedItem is LargeFileInfo file) RecycleLargeFile(file);
    }
    private void FolderTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        var folder = e.NewValue as FolderNodeVM;
        FolderOpen.IsEnabled = folder != null && PathUtil.IsLocalPath(folder.FullPath);
        FolderDetailName.Text = folder?.Name ?? Loc.T("ui.select.folder");
        FolderDetailSize.Text = folder?.SizeText ?? "";
        FolderDetailPath.Text = folder?.FullPath ?? "";
    }
    private void FolderOpen_Click(object sender, RoutedEventArgs e)
    {
        if (FolderTree.SelectedItem is FolderNodeVM folder)
            try { ShellUtil.OpenPath(folder.FullPath); } catch (Exception ex) { _main.ShowToast(ex.Message, ToastType.Warning); }
    }

    private void RecycleLargeFile(LargeFileInfo f)
    {
        if (!Services.DialogService.Confirm(_main, Services.Loc.T("space.recycle.title"),
            string.Format(Services.Loc.T("space.recycle.msg"), System.IO.Path.GetFileName(f.Path), SizeText.OfBytes(f.Size)), Services.Loc.T("btn.recycle")))
            return;
        var ok = ShellUtil.RecycleToBin(f.Path);
        if (ok)
        {
            HistoryService.Add("Clean", string.Format(Services.Loc.T("toast.largecleaned"), System.IO.Path.GetFileName(f.Path)), f.Path, f.Size);
            _main.ShowToast(string.Format(Services.Loc.T("toast.recycled"), SizeText.OfBytes(f.Size)));
            _lastAnalysis?.LargeFiles.Remove(f);
            RenderLargeFiles();
        }
        else
        {
            _main.ShowToast(Services.Loc.T("toast.recyclefailed"), ToastType.Warning);
        }
    }
}

/// <summary>目录树节点 VM。</summary>
public sealed class FolderNodeVM
{
    public string Name { get; }
    public string FullPath { get; }
    public string SizeText { get; }
    public ObservableCollection<FolderNodeVM> Children { get; } = new();

    public FolderNodeVM(string name, long size, string fullPath)
    {
        Name = name;
        FullPath = fullPath;
        SizeText = size > 0 ? CleanOptimize.Core.Utils.SizeText.OfBytes(size) : "";
    }
}
