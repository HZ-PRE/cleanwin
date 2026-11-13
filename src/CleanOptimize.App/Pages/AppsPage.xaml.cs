using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CleanOptimize.Core.Apps;
using CleanOptimize.Core.Store;
using CleanOptimize.Core.Utils;

namespace CleanOptimize.App.Pages;

public partial class AppsPage : Page, IParamPage
{
    private readonly MainWindow _main;
    private readonly AppInventoryService _inventory = new();
    private List<InstalledApp> _apps = new();
    private string _filter = "";
    private bool _loaded;
    private readonly Services.SearchDelay _searchDelay;

    public AppsPage(MainWindow main)
    {
        InitializeComponent();
        _main = main;
        _searchDelay = new Services.SearchDelay(this, Render);
        AppList.RowFactory = item => BuildRow((InstalledApp)item);
        Loaded += (s, e) =>
        {
            if (!_loaded) { _loaded = true; _ = LoadAsync(); }
        };
    }

    public void OnNavigated(object? param) => _ = LoadAsync();

    private Task? _loadTask;
    internal Task LoadAsync()
    {
        _loaded = true;
        return _loadTask is { IsCompleted: false } ? _loadTask : _loadTask = LoadCoreAsync();
    }

    private async Task LoadCoreAsync()
    {
        LoadingPanel.Visibility = Visibility.Visible;
        BtnRefresh.IsEnabled = false;
        WaitingText.Text = Services.Loc.T("apps.loading");
        var selectedKey = (AppList.SelectedItem as InstalledApp)?.RegistryKey;
        AppList.SetItems(null);
        try
        {
            _apps = await Task.Run(() => Services.EnumerationCache.GetApps());
        }
        catch (Exception ex)
        {
            _main.ShowToast(Services.Loc.T("toast.readfailed") + ex.Message, ToastType.Warning);
        }
        LoadingPanel.Visibility = Visibility.Collapsed;
        BtnRefresh.IsEnabled = true;
        Render();
        if (selectedKey != null) AppList.SelectedItem = AppList.Items.Cast<InstalledApp>().FirstOrDefault(a => a.RegistryKey == selectedKey);
    }

    private void Render()
    {
        var selected = AppList.SelectedItem as InstalledApp;
        AppList.SetItems(null);
        var q = _apps.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(_filter))
            q = q.Where(a => a.DisplayName.Contains(_filter, StringComparison.OrdinalIgnoreCase) ||
                             a.Publisher.Contains(_filter, StringComparison.OrdinalIgnoreCase));
        var sort = (SortSelector.SelectedItem as ComboBoxItem)?.Tag as string ?? "name";
        q = sort switch
        {
            "size" => q.OrderByDescending(a => a.EstimatedSize),
            "date" => q.OrderByDescending(a => a.InstallDate),
            _ => q.OrderBy(a => a.DisplayName, StringComparer.CurrentCultureIgnoreCase),
        };
        var list = q.ToList();
        AppList.SetItems(list);
        if (selected != null && list.Contains(selected)) AppList.SelectedItem = selected;
        AppCount.Text = string.Format(Services.Loc.T("ui.apps.count"), list.Count, _apps.Count);
        EmptyText.Visibility = list.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private Border BuildRow(InstalledApp app)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
        text.Children.Add(new TextBlock { Text = app.DisplayName, FontSize = 13,
            FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = (Brush)FindResource("TextPrimaryBrush") });
        text.Children.Add(new TextBlock { Text = app.Publisher, FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 4, 0, 0),
            Foreground = (Brush)FindResource("TextSecondaryBrush") });
        var size = new TextBlock { Text = app.EstimatedSize > 0 ? SizeText.OfBytes(app.EstimatedSize) : Services.Loc.T("apps.unknown"),
            VerticalAlignment = VerticalAlignment.Center, Foreground = (Brush)FindResource("TextSecondaryBrush") };
        var icon = new Controls.AppIcon(app.IconPath, app.IconIndex) { HorizontalAlignment = HorizontalAlignment.Left };
        Grid.SetColumn(text, 1); Grid.SetColumn(size, 2);
        grid.Children.Add(icon); grid.Children.Add(text); grid.Children.Add(size);
        return new Border { Padding = new Thickness(12, 12, 12, 12), Child = grid, ToolTip = app.DisplayName };
    }

    private void AppList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var app = AppList.SelectedItem as InstalledApp;
        AppUninstall.IsEnabled = app != null && !_uninstalling;
        AppDetailName.Text = app?.DisplayName ?? Services.Loc.T("ui.select.app");
        AppDetailIcon.Child = app == null ? null : new Controls.AppIcon(app.IconPath, app.IconIndex);
        AppDetailIcon.Visibility = app == null ? Visibility.Collapsed : Visibility.Visible;
        AppDetailMeta.Text = app == null ? "" : string.Join("\n\n", new[] {
            Services.Loc.T("apps.col.publisher") + ": " + app.Publisher,
            app.DisplayVersion,
            Services.Loc.T("apps.col.size") + ": " + (app.EstimatedSize > 0 ? SizeText.OfBytes(app.EstimatedSize) : Services.Loc.T("apps.unknown")),
            Services.Loc.T("apps.col.date") + ": " + (app.InstallDate == default ? "—" : app.InstallDate.ToString("yyyy-MM-dd")),
            app.IsStoreApp ? Services.Loc.T("apps.store") : app.InstallLocation }.Where(v => !string.IsNullOrWhiteSpace(v)));
    }

    private async void AppUninstall_Click(object sender, RoutedEventArgs e)
    {
        if (_uninstalling || AppList.SelectedItem is not InstalledApp app) return;
        _uninstalling = true;
        AppUninstall.IsEnabled = false;
        try { await UninstallAsync(app); }
        catch (Exception ex) { _main.ShowToast(ex.Message, ToastType.Warning); }
        finally { _uninstalling = false; AppUninstall.IsEnabled = AppList.SelectedItem is InstalledApp; }
    }
    private bool _uninstalling;

    private async Task UninstallAsync(InstalledApp app)
    {
        if (!Services.DialogService.Confirm(_main, string.Format(Services.Loc.T("apps.uninstall.title"), app.DisplayName),
            string.Format(Services.Loc.T("apps.uninstall.msg"), app.EstimatedSize > 0 ? SizeText.OfBytes(app.EstimatedSize) : Services.Loc.T("apps.unknown")), Services.Loc.T("apps.uninstall.start")))
            return;

        var err = _inventory.LaunchUninstall(app);
        if (err != null)
        {
            _main.ShowToast(Services.Loc.T("apps.uninstall.failed") + err, ToastType.Warning);
            return;
        }

        // 等待卸载完成（轮询注册表条目是否消失，最多 5 分钟）
        LoadingPanel.Visibility = Visibility.Visible;
        WaitingText.Text = string.Format(Services.Loc.T("apps.waiting"), app.DisplayName);
        bool gone = false;
        await Task.Run(async () =>
        {
            for (int i = 0; i < 100; i++)
            {
                await Task.Delay(3000);
                try { if (!_inventory.IsStillInstalled(app)) { gone = true; break; } }
                catch { }
            }
        });
        LoadingPanel.Visibility = Visibility.Collapsed;

        if (!gone)
        {
            _main.ShowToast(Services.Loc.T("apps.notdetected"), ToastType.Info);
            return;
        }

        HistoryService.Add("Uninstall", string.Format(Services.Loc.T("apps.uninstalled.log"), app.DisplayName), app.Publisher, app.EstimatedSize);
        _main.ShowToast(string.Format(Services.Loc.T("apps.uninstalled.toast"), app.DisplayName));
        Services.EnumerationCache.InvalidateApps();
        await LoadAsync();

        // 残留扫描
        var leftovers = await Task.Run(() => _inventory.FindLeftovers(app));
        leftovers.RemoveAll(l => !Directory.Exists(l.Path) && !l.IsRegistry);
        if (leftovers.Count > 0)
        {
            ShowLeftoverDialog(app, leftovers);
        }
    }

    private void ShowLeftoverDialog(InstalledApp app, List<LeftoverItem> leftovers)
    {
        if (!_main.CanInteract) return;
        var dlg = new Windows.LeftoverDialog(app.DisplayName, leftovers) { Owner = _main };
        dlg.ShowDialog();
        if (dlg.Confirmed)
        {
            var (ok, fail) = _inventory.RemoveLeftovers(leftovers.Where(l => l.Selected).ToList());
            _main.ShowToast(string.Format(Services.Loc.T("leftover.cleaned"), ok) + (fail > 0 ? string.Format(Services.Loc.T("leftover.fail"), fail) : ""),
                fail > 0 ? ToastType.Warning : ToastType.Success);
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        ClearSearch.Visibility = SearchBox.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        _filter = SearchBox.Text;
        if (_loaded) _searchDelay.Restart();
    }

    private void Sort_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loaded) _searchDelay.Restart();
    }

    private void ClearSearch_Click(object sender, RoutedEventArgs e) { SearchBox.Clear(); SearchBox.Focus(); }

    private void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        if (_loadTask is { IsCompleted: false }) return;
        Services.EnumerationCache.InvalidateApps();
        _ = LoadAsync();
    }
}
