using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CleanOptimize.App.Controls;
using CleanOptimize.Core.Analysis;
using CleanOptimize.Core.Rules;
using CleanOptimize.Core.Store;
using CleanOptimize.Core.Utils;
using Microsoft.Win32;

namespace CleanOptimize.App.Pages;

public partial class ToolboxPage : Page, IParamPage
{
    private readonly MainWindow _main;
    private DuplicateScanResult? _dupResult;
    private bool _dupCleaning;
    private CancellationTokenSource? _dupCts;
    private List<EmptyFolderInfo>? _emptyResult;
    private readonly HashSet<string> _emptySelected = new(StringComparer.OrdinalIgnoreCase);
    private CancellationTokenSource? _emptyCts;
    private bool _emptyCleaning;
    private bool _quarantineBusy;
    private bool _shredding;

    public ToolboxPage(MainWindow main)
    {
        InitializeComponent();
        _main = main;
        QuarantineList.RowFactory = item => BuildQuarantineRow((QuarantineEntry)item);
        EmptyList.RowFactory = item => BuildEmptyRow((EmptyFolderInfo)item);
        DupList.RowFactory = item => BuildDupGroup((DuplicateGroup)item);
        Unloaded += (_, _) => TryCancelCurrent();
    }

    public void OnNavigated(object? param)
    {
        SelectTool(param as string);
        if (param as string == "dup") BtnDupScan_Click(this, new RoutedEventArgs());
        if (param as string == "empty" && _emptyResult == null) BtnEmptyScan_Click(this, new RoutedEventArgs());
    }

    internal void SelectTool(string? tool, bool refresh = true)
    {
        TryCancelCurrent();
        var cards = new[] { QuarantineCard, ShredCard, DuplicateCard, EmptyFolderCard, MaintenanceCard, HistoryCard, WindowsCard };
        bool selected = cards.Any(c => c.Tag as string == tool);
        ToolIndex.Visibility = selected ? Visibility.Collapsed : Visibility.Visible;
        ToolBack.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
        ToolShortcuts.Visibility = selected ? Visibility.Visible : Visibility.Collapsed;
        ToolHeading.Visibility = ToolIntro.Visibility = selected ? Visibility.Collapsed : Visibility.Visible;
        foreach (var button in ToolShortcuts.Children.OfType<Button>())
            button.SetResourceReference(StyleProperty, button.Tag as string == tool ? "PrimaryButton" : "SecondaryButton");
        foreach (var card in cards) card.Visibility = selected && card.Tag as string == tool ? Visibility.Visible : Visibility.Collapsed;
        ToolScroll.ScrollToTop();
        if (!refresh) return;
        if (tool == "quarantine") RefreshQuarantine();
        if (tool == "history") RefreshHistory();
        if (tool == "maint") RefreshMaintenance();
        if (tool == "windows") BuildTools();
    }
    private void ToolTile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button) SelectTool(button.Tag as string);
    }
    private void ToolBack_Click(object sender, RoutedEventArgs e) => SelectTool(null);
    public void TryCancelCurrent() { _dupCts?.Cancel(); _emptyCts?.Cancel(); }

    // ————— 空文件夹清理 —————

    private static List<string> DefaultEmptyDirs()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return new[] { "Pictures", "Videos", "Music", "Downloads", "Documents", "Desktop" }
            .Select(d => Path.Combine(profile, d))
            .Where(Directory.Exists)
            .ToList();
    }

    private List<string> ResolveEmptyDirs()
    {
        var roots = SettingsService.Current.EmptyFolderScanDirs;
        if (roots.Count == 0)
        {
            roots = DefaultEmptyDirs();
            SettingsService.Current.EmptyFolderScanDirs = roots;
            SettingsService.Save();
        }
        return roots.Where(Directory.Exists).ToList();
    }

    private async void BtnEmptyScan_Click(object sender, RoutedEventArgs e)
        => await ScanEmptyAsync();

    internal async Task ScanEmptyAsync(IReadOnlyList<string>? roots = null)
    {
        if (_emptyCts != null || _emptyCleaning) return;
        using var cts = new CancellationTokenSource();
        _emptyCts = cts;
        _emptyResult = null;
        _emptySelected.Clear();
        BtnEmptyScan.IsEnabled = false;
        BtnEmptyDirs.IsEnabled = false;
        BtnEmptyClean.Visibility = Visibility.Collapsed;
        BtnEmptyScan.Content = Services.Loc.T("common.scanning");
        EmptyList.SetItems(null);
        EmptyOutcomeText.Text = "";
        try
        {
            roots ??= ResolveEmptyDirs();
            if (roots.Count == 0) { EmptyOutcomeText.Text = Services.Loc.T("tool.empty.nodirs"); return; }
            EmptySummaryText.Text = string.Join("  ·  ", roots.Select(r => Path.GetFileName(r.TrimEnd('\\'))));
            var svc = new EmptyFolderService();
            _emptyResult = await svc.ScanAsync(roots, 8, cts.Token,
                new Progress<string>(dir => { if (ReferenceEquals(_emptyCts, cts) && !cts.IsCancellationRequested) EmptySummaryText.Text = dir; }));
        }
        catch (OperationCanceledException)
        {
            _emptyResult = null;
        }
        catch (Exception ex)
        {
            EmptyOutcomeText.Text = Services.Loc.T("toast.scanfailed") + ex.Message;
            _main.ShowToast(EmptyOutcomeText.Text, ToastType.Warning);
            _emptyResult = null;
        }
        finally
        {
            if (ReferenceEquals(_emptyCts, cts)) _emptyCts = null;
            BtnEmptyScan.IsEnabled = BtnEmptyDirs.IsEnabled = true;
            BtnEmptyScan.Content = Services.Loc.T("tool.empty.rescan");
            RenderEmpty();
        }
    }

    private void RenderEmpty()
    {
        EmptyList.SetItems(null);
        _emptySelected.Clear();
        BtnEmptyClean.Visibility = Visibility.Collapsed;
        if (_emptyResult == null) { EmptySummaryText.Text = ""; return; }

        int totalSubs = _emptyResult.Sum(f => f.SubFolderCount);
        EmptySummaryText.Text = _emptyResult.Count == 0
            ? Services.Loc.T("tool.empty.none")
            : string.Format(Services.Loc.T("tool.empty.summary"), _emptyResult.Count, totalSubs);
        if (_emptyResult.Count == 0) return;
        BtnEmptyClean.Visibility = Visibility.Visible;

        foreach (var f in _emptyResult) _emptySelected.Add(f.Path);
        EmptyList.SetItems(_emptyResult);
    }

    private Border BuildEmptyRow(EmptyFolderInfo f)
    {
        var row = new Border
        {
            Background = (Brush)FindResource("ItemHoverBrush"),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 4, 0, 0),
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var cb = new CheckBox
        {
            Style = (Style)FindResource("RoundCheckOnly"),
            IsChecked = _emptySelected.Contains(f.Path),
            VerticalAlignment = VerticalAlignment.Center,
        };
        cb.Checked += (_, _) => _emptySelected.Add(f.Path);
        cb.Unchecked += (_, _) => _emptySelected.Remove(f.Path);
        Grid.SetColumn(cb, 0);

        var mid = new StackPanel { Margin = new Thickness(12, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        var name = new TextBlock
        {
            Text = f.Path,
            FontSize = 12,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        ToolTipService.SetToolTip(name, f.Path);
        UiPathMenu.Attach(name, f.Path);
        mid.Children.Add(name);
        mid.Children.Add(new TextBlock
        {
            Text = f.SubFolderCount > 0
                ? string.Format(Services.Loc.T("tool.empty.item.f"), f.SubFolderCount, f.LastWriteTime.ToString("yyyy-MM-dd"))
                : string.Format(Services.Loc.T("tool.empty.item.nosub"), f.SubFolderCount, f.LastWriteTime.ToString("yyyy-MM-dd")),
            FontSize = 11,
            Foreground = (Brush)FindResource("TextTertiaryBrush"),
            Margin = new Thickness(0, 2, 0, 0),
        });
        Grid.SetColumn(mid, 1);

        grid.Children.Add(cb);
        grid.Children.Add(mid);
        row.Child = grid;
        return row;
    }

    private void BtnEmptyDirs_Click(object sender, RoutedEventArgs e)
    {
        if (_emptyCts != null || _emptyCleaning) return;
        var dlg = new System.Windows.Forms.FolderBrowserDialog { Description = Services.Loc.T("tool.empty.pickdir") };
        if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        var roots = new List<string> { dlg.SelectedPath };
        SettingsService.Current.EmptyFolderScanDirs = roots;
        SettingsService.Save();
        _main.ShowToast(string.Format(Services.Loc.T("tool.empty.rootsset"), dlg.SelectedPath), ToastType.Info);
        BtnEmptyScan_Click(this, new RoutedEventArgs());
    }

    private async void BtnEmptyClean_Click(object sender, RoutedEventArgs e)
    {
        if (_emptyResult == null || _emptyCleaning || _emptyCts != null) return;
        var selected = _emptyResult.Where(f => _emptySelected.Contains(f.Path)).ToList();
        if (selected.Count == 0)
        {
            _main.ShowToast(Services.Loc.T("tool.empty.nosel"), ToastType.Info);
            return;
        }
        if (!Services.DialogService.Confirm(_main, Services.Loc.T("tool.empty.clean.title"),
            string.Format(Services.Loc.T("tool.empty.clean.msg"), selected.Count), Services.Loc.T("btn.recycle")))
            return;

        await CleanEmptyAsync(selected.Select(f => f.Path).ToArray());
    }

    internal async Task CleanEmptyAsync(IReadOnlyList<string> selected)
    {
        if (_emptyCleaning || _emptyCts != null) return;
        _emptyCleaning = true;
        BtnEmptyClean.IsEnabled = BtnEmptyScan.IsEnabled = BtnEmptyDirs.IsEnabled = EmptyList.IsEnabled = false;
        try
        {
            var result = await EmptyFolderService.RecycleSelectedAsync(selected);
            var removed = result.RecycledPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
            _emptyResult?.RemoveAll(f => removed.Contains(f.Path));
            RenderEmpty();
            ShowOperationResult(EmptyOutcomeText, removed.Count, result.Failures.Select(f => f.Path + " — " + f.Reason).ToList());
            HistoryService.Add("Clean", EmptyOutcomeText.Text, "", 0);
        }
        catch (Exception ex) { EmptyOutcomeText.Text = ex.Message; _main.ShowToast(ex.Message, ToastType.Warning); }
        finally { _emptyCleaning = false; BtnEmptyClean.IsEnabled = BtnEmptyScan.IsEnabled = BtnEmptyDirs.IsEnabled = EmptyList.IsEnabled = true; }
    }

    private void ShowOperationResult(TextBlock target, int completed, IReadOnlyList<string> failures)
    {
        var summary = string.Format(Services.Loc.T("ui.operation.result"), completed, failures.Count);
        target.Text = summary + (failures.Count == 0 ? "" : "\n" + string.Join("\n", failures.Take(5)));
        _main.ShowToast(summary, failures.Count == 0 ? ToastType.Success : ToastType.Warning);
    }

    // ————— 隔离区 —————

    private void RefreshQuarantine()
    {
        try { RefreshQuarantineCore(); }
        catch (Exception ex)
        {
            QuarantineList.SetItems(null);
            BtnPurgeQuarantine.IsEnabled = false;
            QuarantineOutcomeText.Text = ex.Message;
            _main.ShowToast(ex.Message, ToastType.Warning);
        }
    }

    private void RefreshQuarantineCore()
    {
        var entries = QuarantineService.Instance.Entries;
        QuarantineList.SetItems(null);
        long total = entries.Sum(e => e.Size);
        QuarantineSummaryText.Text = entries.Count == 0
            ? Services.Loc.T("tool.quar.empty")
            : string.Format(Services.Loc.T("tool.quar.summary"), entries.Count, SizeText.OfBytes(total), SettingsService.Current.QuarantineDays);
        BtnPurgeQuarantine.IsEnabled = !_quarantineBusy && entries.Count > 0;

        QuarantineList.SetItems(entries.OrderByDescending(x => x.CreatedAt).ToList());
    }

    private Border BuildQuarantineRow(QuarantineEntry e)
    {
        var row = new Border
        {
            Background = (Brush)FindResource("ItemHoverBrush"),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(12, 9, 12, 9),
            Margin = new Thickness(0, 4, 0, 0),
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var name = System.IO.Path.GetFileName(e.OriginalPath.TrimEnd('\\'));
        sp.Children.Add(new TextBlock
        {
            Text = name,
            FontSize = 12.5,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        var meta = new TextBlock
        {
            Text = string.Format(Services.Loc.T("tool.quar.meta"), e.OriginalPath, e.CreatedAt.ToString("yyyy-MM-dd HH:mm")),
            FontSize = 11,
            Foreground = (Brush)FindResource("TextTertiaryBrush"),
            Margin = new Thickness(0, 2, 0, 0),
        };
        ToolTipService.SetToolTip(meta, e.OriginalPath);
        UiPathMenu.Attach(sp, e.OriginalPath);
        sp.Children.Add(meta);
        Grid.SetColumn(sp, 0);

        var size = new TextBlock
        {
            Text = SizeText.OfBytes(e.Size),
            FontSize = 12.5,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(size, 1);

        var remain = new TextBlock
        {
            Text = string.Format(Services.Loc.T("tool.quar.daysleft"), Math.Max(0, (e.ExpireAt - DateTime.Now).Days)),
            FontSize = 11,
            Foreground = (Brush)FindResource("TextTertiaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(14, 0, 0, 0),
        };
        Grid.SetColumn(remain, 2);

        var restoreBtn = new Button
        {
            Content = Services.Loc.T("btn.restore"),
            Style = (Style)FindResource("SmallPrimaryButton"),
            Margin = new Thickness(14, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        restoreBtn.Click += (s, a) =>
        {
            if (_quarantineBusy) return;
            var err = QuarantineService.Instance.Restore(e.Id);
            if (err == null) _main.ShowToast(string.Format(Services.Loc.T("toast.restored"), name));
            else _main.ShowToast(Services.Loc.T("toast.restorefailed") + err, ToastType.Warning);
            RefreshQuarantine();
        };
        Grid.SetColumn(restoreBtn, 3);

        var delBtn = new Button
        {
            Content = Services.Loc.T("btn.deleteperm"),
            Style = (Style)FindResource("SmallSecondaryButton"),
            Foreground = (Brush)FindResource("DangerBrush"),
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        delBtn.Click += async (s, a) =>
        {
            if (_quarantineBusy) return;
            if (!Services.DialogService.Confirm(_main, Services.Loc.T("dlg.deleteperm.title"), string.Format(Services.Loc.T("dlg.deleteperm.msg"), name), Services.Loc.T("btn.deleteperm"), Services.Loc.T("btn.cancel"), danger: true))
                return;
            _quarantineBusy = true;
            QuarantineList.IsEnabled = BtnPurgeQuarantine.IsEnabled = false;
            try
            {
                var result = await Task.Run(() => { var ok = QuarantineService.Instance.DeletePermanent(e.Id, out _, out var error); return (ok, error); });
                ShowOperationResult(QuarantineOutcomeText, result.ok ? 1 : 0, result.ok ? Array.Empty<string>() : new[] { result.error ?? "删除失败" });
            }
            catch (Exception ex) { QuarantineOutcomeText.Text = ex.Message; _main.ShowToast(ex.Message, ToastType.Warning); }
            finally { _quarantineBusy = false; QuarantineList.IsEnabled = true; RefreshQuarantine(); }
        };
        Grid.SetColumn(delBtn, 4);

        grid.Children.Add(sp);
        grid.Children.Add(size);
        grid.Children.Add(remain);
        grid.Children.Add(restoreBtn);
        grid.Children.Add(delBtn);
        row.Child = grid;
        return row;
    }

    private async void BtnPurgeQuarantine_Click(object sender, RoutedEventArgs e)
    {
        if (_quarantineBusy) return;
        try
        {
            var n = QuarantineService.Instance.Entries.Count;
            if (n == 0) return;
            if (!Services.DialogService.Confirm(_main, Services.Loc.T("tool.purge.title"),
                string.Format(Services.Loc.T("tool.purge.msg"), n), Services.Loc.T("btn.deleteperm"), Services.Loc.T("btn.cancel"), danger: true)) return;
            await PurgeQuarantineAsync();
        }
        catch (Exception ex) { QuarantineOutcomeText.Text = ex.Message; _main.ShowToast(ex.Message, ToastType.Warning); }
    }

    internal async Task PurgeQuarantineAsync()
    {
        if (_quarantineBusy) return;
        _quarantineBusy = true;
        BtnPurgeQuarantine.IsEnabled = QuarantineList.IsEnabled = false;
        try
        {
            var result = await Task.Run(() => QuarantineService.Instance.PurgeAllDetailed());
            ShowOperationResult(QuarantineOutcomeText, result.DeletedCount, result.Failures.Select(f => f.Reason).ToList());
        }
        finally { _quarantineBusy = false; QuarantineList.IsEnabled = true; RefreshQuarantine(); }
    }

    // ————— 系统维护状态 —————

    private async void RefreshMaintenance()
    {
        BtnMaintRefresh.IsEnabled = false;
        try
        {
            var items = await Task.Run(() => CleanOptimize.Core.Analysis.SystemMaintenanceService.CheckAll());
            MaintList.Children.Clear();
            foreach (var it in items)
            {
                MaintList.Children.Add(BuildMaintRow(it));
            }
        }
        finally
        {
            BtnMaintRefresh.IsEnabled = true;
        }
    }

    private Border BuildMaintRow(CleanOptimize.Core.Analysis.MaintenanceItem it)
    {
        var row = new Border
        {
            Background = (Brush)FindResource("ItemHoverBrush"),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(12, 9, 12, 9),
            Margin = new Thickness(0, 4, 0, 0),
        };
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var (stateText, stateBrush, stateSoft) = it.State switch
        {
            "on" => (Services.Loc.T("maint.state.on"), (Brush)FindResource("SuccessBrush"), (Brush)FindResource("SuccessSoftBrush")),
            "off" => (Services.Loc.T("maint.state.off"), (Brush)FindResource("WarningBrush"), (Brush)FindResource("WarningSoftBrush")),
            _ => (Services.Loc.T("maint.state.unknown"), (Brush)FindResource("TextSecondaryBrush"), (Brush)FindResource("ItemHoverBrush")),
        };
        var chip = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(9, 3, 9, 4),
            Background = stateSoft,
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 58,
        };
        chip.Child = new TextBlock
        {
            Text = stateText,
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Foreground = stateBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        Grid.SetColumn(chip, 0);

        var mid = new StackPanel { Margin = new Thickness(14, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        mid.Children.Add(new TextBlock
        {
            Text = Services.Loc.T($"maint.{it.Id}.name"),
            FontSize = 12.5,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
        });
        if (!string.IsNullOrEmpty(it.Detail))
        {
            mid.Children.Add(new TextBlock
            {
                Text = string.Format(Services.Loc.T("maint.detail.plan"), it.Detail),
                FontSize = 11,
                Foreground = (Brush)FindResource("TextTertiaryBrush"),
                Margin = new Thickness(0, 2, 0, 0),
            });
        }
        Grid.SetColumn(mid, 1);

        var desc = new TextBlock
        {
            Text = Services.Loc.T($"maint.{it.Id}.desc"),
            FontSize = 11.5,
            Foreground = (Brush)FindResource("TextTertiaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 14, 0),
        };
        Grid.SetColumn(desc, 2);

        if (!string.IsNullOrEmpty(it.TargetUri))
        {
            var go = new Button
            {
                Content = Services.Loc.T("maint.goto"),
                Style = (Style)FindResource("SmallSecondaryButton"),
                VerticalAlignment = VerticalAlignment.Center,
            };
            go.Click += (s, e) =>
            {
                try
                {
                    var psi = new ProcessStartInfo(it.TargetUri) { UseShellExecute = true };
                    Process.Start(psi);
                }
                catch (Exception ex) { _main.ShowToast(Services.Loc.T("tool.win.openfail") + ex.Message, ToastType.Warning); }
            };
            Grid.SetColumn(go, 3);
            grid.Children.Add(go);
        }

        grid.Children.Add(chip);
        grid.Children.Add(mid);
        grid.Children.Add(desc);
        row.Child = grid;
        return row;
    }

    private void BtnMaintRefresh_Click(object sender, RoutedEventArgs e) => RefreshMaintenance();

    // ————— 文件粉碎 —————

    private void BtnShredFile_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Title = Services.Loc.T("tool.shred.pickfile"), Multiselect = false };
        if (dlg.ShowDialog() != true) return;
        Shred(dlg.FileName, isDir: false);
    }

    private void BtnShredDir_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new System.Windows.Forms.FolderBrowserDialog { Description = Services.Loc.T("tool.shred.pickdir") };
        if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        Shred(dlg.SelectedPath, isDir: true);
    }

    private async void Shred(string path, bool isDir)
    {
        if (_shredding) return;
        _shredding = true;
        BtnShredFile.IsEnabled = BtnShredDir.IsEnabled = false;
        try
        {
            var deny = CleanOptimize.Core.Security.SafetyGuard.ValidateForExplicitShred(path, path,
                SettingsService.Current.Whitelist.ToHashSet(StringComparer.OrdinalIgnoreCase));
            if (deny != null) { ShredOutcomeText.Text = deny; _main.ShowToast(deny, ToastType.Warning); return; }
            var size = await Task.Run(() => isDir ? FileUtil.DirSize(path) : new FileInfo(path).Length);
            if (!Services.DialogService.Confirm(_main, Services.Loc.T("tool.shred.confirm.title"),
                string.Format(Services.Loc.T("tool.shred.confirm.msg"), path, SizeText.OfBytes(size)),
                Services.Loc.T("btn.shred"), Services.Loc.T("btn.cancel"), danger: true)) return;
            var result = await Task.Run(() => { var ok = CleanOptimize.Core.Security.Shredder.ShredSelected(path, out var error); return (ok, error); });
            ShredOutcomeText.Text = result.ok ? Services.Loc.T("toast.shredok") : Services.Loc.T("toast.shrefail") + "\n" + result.error;
            HistoryService.Add("Shred", ShredOutcomeText.Text, path, result.ok ? size : 0);
            _main.ShowToast(ShredOutcomeText.Text, result.ok ? ToastType.Success : ToastType.Warning);
        }
        catch (Exception ex) { ShredOutcomeText.Text = ex.Message; _main.ShowToast(ex.Message, ToastType.Warning); }
        finally { _shredding = false; BtnShredFile.IsEnabled = BtnShredDir.IsEnabled = true; }
    }

    // ————— 重复文件 —————

    private async void BtnDupScan_Click(object sender, RoutedEventArgs e)
    {
        if (_dupCleaning) return;
        // 二态:扫描中再次点击 = 取消
        if (_dupCts != null)
        {
            _dupCts.Cancel();
            return;
        }
        await ScanDuplicatesAsync();
    }

    private async Task ScanDuplicatesAsync()
    {
        var roots = SettingsService.Current.DuplicateScanDirs;
        if (roots.Count == 0)
        {
            roots = DefaultDupDirs();
            SettingsService.Current.DuplicateScanDirs = roots;
            SettingsService.Save();
        }
        roots = roots.Where(Directory.Exists).ToList();
        if (roots.Count == 0)
        {
            _main.ShowToast(Services.Loc.T("tool.dup.nodirs"), ToastType.Info);
            return;
        }

        _dupCts = new CancellationTokenSource();
        _dupResult = null;
        DupOutcomeText.Text = "";
        BtnDupClean.IsEnabled = false;
        BtnDupKeepNewest.IsEnabled = false;
        BtnDupScan.Content = Services.Loc.T("btn.stopscan");
        BtnDupDirs.IsEnabled = false;
        DupList.SetItems(null);
        DupSummaryText.Text = Services.Loc.T("common.scanning");
        DuplicateScanResult? result = null;
        try
        {
            var svc = new DuplicateFileService();
            result = await svc.ScanAsync(roots, 1024 * 1024, _dupCts.Token,
                new Progress<(string dir, int files)>(p => DupSummaryText.Text = string.Format(Services.Loc.T("tool.dup.progress"), p.files)));
        }
        catch (OperationCanceledException)
        {
            _main.ShowToast(Services.Loc.T("toast.scanstopped"), ToastType.Info);
        }
        catch (Exception ex)
        {
            _main.ShowToast(Services.Loc.T("toast.scanfailed") + ex.Message, ToastType.Warning);
        }
        _dupCts.Dispose();
        _dupCts = null;
        BtnDupScan.Content = Services.Loc.T("btn.dup.rescan");
        BtnDupDirs.IsEnabled = true;
        BtnDupClean.IsEnabled = true;
        BtnDupKeepNewest.IsEnabled = true;
        _dupResult = result;
        if (_dupResult == null) DupSummaryText.Text = "";
        RenderDup();
    }

    private static List<string> DefaultDupDirs()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return new[] { "Pictures", "Videos", "Downloads" }
            .Select(d => Path.Combine(profile, d))
            .Where(Directory.Exists)
            .ToList();
    }

    private void RenderDup()
    {
        DupList.SetItems(null);
        BtnDupKeepNewest.Visibility = Visibility.Collapsed;
        BtnDupClean.Visibility = Visibility.Collapsed;
        if (_dupResult == null) return;
        var groups = _dupResult.Groups.Where(g => g.Files.Count > 1).ToList();
        DupSummaryText.Text = groups.Count == 0
            ? Services.Loc.T("tool.dup.none")
            : string.Format(Services.Loc.T("tool.dup.summary"), groups.Count, SizeText.OfBytes(_dupResult.TotalWastedBytes));
        if (groups.Count == 0) return;
        BtnDupKeepNewest.Visibility = Visibility.Visible;
        BtnDupClean.Visibility = Visibility.Visible;

        DupList.SetItems(groups);
    }

    private Border BuildDupGroup(DuplicateGroup g)
    {
        var card = new Border
        {
            Background = (Brush)FindResource("ItemHoverBrush"),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(14, 10, 14, 10),
            Margin = new Thickness(0, 6, 0, 0),
        };
        var sp = new StackPanel();
        sp.Children.Add(new TextBlock
        {
            Text = string.Format(Services.Loc.T("tool.dup.group"), g.Files.Count, SizeText.OfBytes(g.Size), SizeText.OfBytes(g.WastedBytes)),
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
        });
        var files = new VirtualRows { Height = Math.Min(320, g.Files.Count * 64) };
        files.RowFactory = item =>
        {
            var f = (DuplicateFile)item;
            var line = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            var cb = new CheckBox
            {
                Style = (Style)FindResource("RoundCheckOnly"),
                IsChecked = f.Selected,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = Services.Loc.T("tool.dup.tip"),
            };
            cb.Checked += (s, e) => f.Selected = true;
            cb.Unchecked += (s, e) => f.Selected = false;
            line.Children.Add(cb);
            var sp2 = new StackPanel { Margin = new Thickness(10, 0, 0, 0) };
            var nameTb = new TextBlock
            {
                Text = System.IO.Path.GetFileName(f.Path),
                FontSize = 12,
                Foreground = (Brush)FindResource("TextPrimaryBrush"),
            };
            UiPathMenu.Attach(nameTb, f.Path);
            sp2.Children.Add(nameTb);
            var path = new TextBlock
            {
                Text = f.Path + $" · {f.ModifiedTime:yyyy-MM-dd HH:mm}",
                FontSize = 11,
                Foreground = (Brush)FindResource("TextTertiaryBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 760,
            };
            ToolTipService.SetToolTip(path, f.Path);
            sp2.Children.Add(path);
            line.Children.Add(sp2);
            return line;
        };
        files.SetItems(g.Files);
        sp.Children.Add(files);
        card.Child = sp;
        return card;
    }

    private void BtnDupDirs_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new System.Windows.Forms.FolderBrowserDialog { Description = Services.Loc.T("tool.dup.pickdir") };
        if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        SettingsService.Current.DuplicateScanDirs = new List<string> { dlg.SelectedPath };
        SettingsService.Save();
        _main.ShowToast(string.Format(Services.Loc.T("tool.dup.rootsset"), dlg.SelectedPath), ToastType.Info);
        BtnDupScan_Click(this, new RoutedEventArgs());
    }

    private void BtnDupKeepNewest_Click(object sender, RoutedEventArgs e)
    {
        if (_dupResult == null || _dupCleaning || _dupCts != null) return;
        DuplicateFileService.AutoSelect(_dupResult.Groups.Where(g => g.Files.Count > 1), keepNewest: true);
        RenderDup();
    }

    private async void BtnDupClean_Click(object sender, RoutedEventArgs e)
    {
        if (_dupResult == null || _dupCleaning || _dupCts != null) return;
        var selected = _dupResult.Groups.SelectMany(g => g.Files).Where(f => f.Selected).ToList();
        if (selected.Count == 0)
        {
            _main.ShowToast(Services.Loc.T("tool.dup.nosel"), ToastType.Info);
            return;
        }
        if (_dupResult.Groups.Any(g => g.Files.Count > 1 && g.Files.All(f => f.Selected)))
        {
            _main.ShowToast(Services.Loc.T("ui.duplicate.keep"), ToastType.Warning);
            return;
        }
        long bytes = selected.Sum(f => f.Size);
        if (!Services.DialogService.Confirm(_main, Services.Loc.T("tool.dup.clean.title"),
            string.Format(Services.Loc.T("tool.dup.clean.msg"), selected.Count, SizeText.OfBytes(bytes)), Services.Loc.T("btn.recycle")))
            return;
        _dupCleaning = true;
        BtnDupClean.IsEnabled = BtnDupScan.IsEnabled = BtnDupDirs.IsEnabled = BtnDupKeepNewest.IsEnabled = DupList.IsEnabled = false;
        DupOutcomeText.Text = Services.Loc.T("ui.duplicate.running");
        try
        {
            var outcome = await DuplicateRecycleService.RecycleSelectedAsync(_dupResult.Groups);
            var removed = outcome.RecycledPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var group in _dupResult.Groups) group.Files.RemoveAll(f => removed.Contains(f.Path));
            var summary = string.Format(Services.Loc.T("ui.duplicate.result"), removed.Count,
                SizeText.OfBytes(outcome.RecycledBytes), outcome.Failures.Count);
            DupOutcomeText.Text = summary;
            if (outcome.Failures.Count > 0)
            {
                DupOutcomeText.Text += "\n" + string.Join("\n", outcome.Failures.Take(5).Select(f => f.Path + " — " + f.Reason));
            }
            HistoryService.Add("Clean", summary, string.Join("\n", outcome.Failures.Take(5).Select(f => f.Path + " — " + f.Reason)), outcome.RecycledBytes);
            RenderDup();
            _main.ShowToast(summary, outcome.Failures.Count > 0 ? ToastType.Warning : ToastType.Success);
        }
        catch (Exception ex)
        {
            DupOutcomeText.Text = ex.Message;
            _main.ShowToast(ex.Message, ToastType.Warning);
        }
        finally
        {
            _dupCleaning = false;
            BtnDupClean.IsEnabled = BtnDupScan.IsEnabled = BtnDupDirs.IsEnabled = BtnDupKeepNewest.IsEnabled = DupList.IsEnabled = true;
        }
    }

    // ————— 历史 —————

    private void RefreshHistory()
    {
        HistoryList.Children.Clear();
        var acts = HistoryService.LoadAll().Take(30).ToList();
        if (acts.Count == 0)
        {
            HistoryList.Children.Add(new TextBlock
            {
                Text = Services.Loc.T("history.empty"),
                FontSize = 12,
                Foreground = (Brush)FindResource("TextTertiaryBrush"),
            });
            return;
        }
        foreach (var a in acts)
        {
            var line = new Grid { Margin = new Thickness(0, 5, 0, 5) };
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var typeChip = new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 2, 8, 3),
                VerticalAlignment = VerticalAlignment.Center,
                Background = (Brush)FindResource("PrimarySoftBrush"),
            };
            typeChip.Child = new TextBlock
            {
                Text = HistoryTypeText.Of(a.Type),
                FontSize = 10.5,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)FindResource("PrimaryBrush"),
            };
            Grid.SetColumn(typeChip, 0);

            var title = new TextBlock
            {
                Text = a.Title + (string.IsNullOrEmpty(a.Detail) ? "" : $"（{a.Detail}）"),
                FontSize = 12.5,
                Foreground = (Brush)FindResource("TextPrimaryBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 8, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            Grid.SetColumn(title, 1);

            var bytes = new TextBlock
            {
                Text = a.Bytes > 0 ? SizeText.OfBytes(a.Bytes) : "",
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)FindResource("SuccessBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 14, 0),
            };
            Grid.SetColumn(bytes, 2);

            var time = new TextBlock
            {
                Text = a.Time.ToString("MM-dd HH:mm"),
                FontSize = 11,
                Foreground = (Brush)FindResource("TextTertiaryBrush"),
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(time, 3);

            line.Children.Add(typeChip);
            line.Children.Add(title);
            line.Children.Add(bytes);
            line.Children.Add(time);
            HistoryList.Children.Add(line);
        }
    }

    private void BtnClearHistory_Click(object sender, RoutedEventArgs e)
    {
        if (!Services.DialogService.Confirm(_main, Services.Loc.T("history.clear.title"), Services.Loc.T("history.clear.msg"), Services.Loc.T("btn.clearhistory")))
            return;
        HistoryService.Clear();
        RefreshHistory();
    }

    // ————— Windows 快捷工具 —————

    private void BuildTools()
    {
        if (ToolGrid.Children.Count > 0) return;
        void AddTool(string name, string desc, Action launch)
        {
            var btn = new Button
            {
                Cursor = System.Windows.Input.Cursors.Hand,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                Margin = new Thickness(0, 0, 10, 10),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
            };
            btn.Style = (Style)FindResource("SecondaryButton");
            var sp = new StackPanel { Margin = new Thickness(14, 12, 8, 12) };
            sp.Children.Add(new TextBlock
            {
                Text = name,
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)FindResource("TextPrimaryBrush"),
            });
            sp.Children.Add(new TextBlock
            {
                Text = desc,
                FontSize = 11,
                Foreground = (Brush)FindResource("TextTertiaryBrush"),
                Margin = new Thickness(0, 3, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            btn.Content = sp;
            btn.Click += (s, e) => { try { launch(); } catch (Exception ex) { _main.ShowToast(Services.Loc.T("tool.win.openfail") + ex.Message, ToastType.Warning); } };
            ToolGrid.Children.Add(btn);
        }

        AddTool(Services.Loc.T("tool.win.taskmgr"), Services.Loc.T("tool.win.taskmgr.d"), () => ShellUtil.OpenSystemTool("taskmgr.exe"));
        AddTool(Services.Loc.T("tool.win.storage"), Services.Loc.T("tool.win.storage.d"), () => Process.Start(new ProcessStartInfo("ms-settings:storagesense") { UseShellExecute = true }));
        AddTool(Services.Loc.T("tool.win.power"), Services.Loc.T("tool.win.power.d"), () => ShellUtil.OpenSystemTool("powercfg.cpl"));
        AddTool(Services.Loc.T("tool.win.dns"), Services.Loc.T("tool.win.dns.d"), () => Services.QuickActions.FlushDns(_main));
        AddTool(Services.Loc.T("tool.win.diskmgmt"), Services.Loc.T("tool.win.diskmgmt.d"), () => ShellUtil.OpenSystemTool("diskmgmt.msc"));
        AddTool(Services.Loc.T("tool.win.devmgmt"), Services.Loc.T("tool.win.devmgmt.d"), () => ShellUtil.OpenSystemTool("devmgmt.msc"));
        AddTool(Services.Loc.T("tool.win.control"), Services.Loc.T("tool.win.control.d"), () => ShellUtil.OpenSystemTool("control.exe"));
        AddTool(Services.Loc.T("tool.win.msinfo"), Services.Loc.T("tool.win.msinfo.d"), () => ShellUtil.OpenSystemTool("msinfo32.exe"));
        AddTool(Services.Loc.T("tool.win.update"), Services.Loc.T("tool.win.update.d"), () => Process.Start(new ProcessStartInfo("ms-settings:windowsupdate") { UseShellExecute = true }));
        AddTool(Services.Loc.T("tool.win.restore"), Services.Loc.T("tool.win.restore.d"), () => ShellUtil.OpenSystemTool("rstrui.exe"));
    }
}
