using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using CleanOptimize.App.Services;
using CleanOptimize.Core.Store;

namespace CleanOptimize.App.Pages;

public partial class SettingsPage : Page
{
    private readonly MainWindow _main;
    private readonly AppSettings _s = SettingsService.Current;

    public SettingsPage(MainWindow main)
    {
        InitializeComponent();
        _main = main;
        AboutVersion.Text = "v" + (System.Reflection.Assembly.GetEntryAssembly()?
            .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? "1.0.0");
        BuildUi();
    }

    private void Save()
    {
        SettingsService.Save();
        StartupRegistration.Sync(_s.LaunchAtStartup);
    }

    internal void SelectSection(string section)
    {
        foreach (var card in SettingsSections.Children.OfType<Border>())
            card.Visibility = card.Tag as string == section ? Visibility.Visible : Visibility.Collapsed;
        SettingsScroll.ScrollToTop();
    }

    private void SettingsTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton button && button.Tag is string section) SelectSection(section);
    }

    private StackPanel Row(string title, string? desc, UIElement control)
    {
        var row = new Grid { Margin = new Thickness(0, 6, 0, 6) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        sp.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 13,
            Foreground = (Brush)FindResource("TextPrimaryBrush"),
        });
        if (!string.IsNullOrEmpty(desc))
            sp.Children.Add(new TextBlock
            {
                Text = desc,
                FontSize = 11.5,
                Foreground = (Brush)FindResource("TextTertiaryBrush"),
                Margin = new Thickness(0, 3, 0, 0),
                TextWrapping = TextWrapping.Wrap,
            });
        Grid.SetColumn(sp, 0);
        Grid.SetColumn(control, 1);
        control.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        row.Children.Add(sp);
        row.Children.Add(control);
        return new StackPanel { Children = { row } };
    }

    private ToggleButton Toggle(bool value, Action<bool> onChanged)
    {
        var t = new ToggleButton { Style = (Style)FindResource("ToggleSwitch"), IsChecked = value };
        t.Click += (s, e) => { onChanged(t.IsChecked == true); Save(); };
        return t;
    }

    private void BuildUi()
    {
        // —— 通用 ——
        GeneralSection.Children.Add(Row(Services.Loc.T("settings.row.autostart"), Services.Loc.T("settings.row.autostart.d"),
            Toggle(_s.LaunchAtStartup, v => _s.LaunchAtStartup = v)));
        GeneralSection.Children.Add(Row(Services.Loc.T("settings.row.mintotray"), Services.Loc.T("settings.row.mintotray.d"),
            Toggle(_s.MinimizeToTray, v => _s.MinimizeToTray = v)));
        GeneralSection.Children.Add(Row(Services.Loc.T("settings.row.closetotray"), Services.Loc.T("settings.row.closetotray.d"),
            Toggle(_s.CloseToTray, v => _s.CloseToTray = v)));

        var themeBox = new ComboBox { MinWidth = 130 };
        foreach (var (label, val) in new[] { (Services.Loc.T("settings.theme.light"), "light"), (Services.Loc.T("settings.theme.dark"), "dark"), (Services.Loc.T("settings.theme.system"), "system") })
        {
            var item = new ComboBoxItem { Content = label, Tag = val };
            themeBox.Items.Add(item);
            if (_s.Theme == val) item.IsSelected = true;
        }
        themeBox.SelectionChanged += (s, e) =>
        {
            if (themeBox.SelectedItem is ComboBoxItem it)
            {
                _s.Theme = (string)it.Tag!;
                Save();
                Services.ThemeManager.ApplyFromSettings();
            }
        };
        // —— 语言选择 ——
        var langBox = new ComboBox { MinWidth = 150 };
        var langCodes = new List<string> { "auto" };
        langBox.Items.Add(Services.Loc.T("settings.lang.auto"));
        foreach (var (code, native) in Services.Loc.Supported)
        {
            langCodes.Add(code);
            langBox.Items.Add(native);
        }
        var current = string.IsNullOrWhiteSpace(_s.Language) ? "auto" : _s.Language;
        var curIdx = langCodes.IndexOf(current);
        if (curIdx < 0) curIdx = 0;
        langBox.SelectedIndex = curIdx;
        langBox.SelectionChanged += (s2, e2) =>
        {
            if (langBox.SelectedIndex >= 0)
            {
                _s.Language = langCodes[langBox.SelectedIndex];
                SettingsService.Save();
                Services.Loc.SetLanguage(_s.Language);
                _main.ShowToast(Services.Loc.T("toast.langapplied"), ToastType.Info);
            }
        };
        GeneralSection.Children.Add(Row(Services.Loc.T("settings.row.language"), Services.Loc.T("settings.row.language.d"), langBox));

        GeneralSection.Children.Add(Row(Services.Loc.T("settings.row.theme"), "", themeBox));

        // —— 清理 ——
        CleanSection.Children.Add(Row(Services.Loc.T("settings.row.quarantine"), Services.Loc.T("settings.row.quarantine.d"),
            Toggle(_s.QuarantineEnabled, v => _s.QuarantineEnabled = v)));

        var daysBox = new ComboBox { MinWidth = 130 };
        foreach (var d in new[] { 7, 15, 30 })
        {
            var item = new ComboBoxItem { Content = d + " " + Services.Loc.T("settings.days"), Tag = d };
            daysBox.Items.Add(item);
            if (_s.QuarantineDays == d) item.IsSelected = true;
        }
        daysBox.SelectionChanged += (s, e) =>
        {
            if (daysBox.SelectedItem is ComboBoxItem it) { _s.QuarantineDays = (int)it.Tag!; Save(); }
        };
        CleanSection.Children.Add(Row(Services.Loc.T("settings.row.quarantine.days"), Services.Loc.T("settings.row.quarantine.days.d"), daysBox));

        CleanSection.Children.Add(Row(Services.Loc.T("settings.row.autoclean"), Services.Loc.T("settings.row.autoclean.d"),
            Toggle(_s.AutoCleanEnabled, v => _s.AutoCleanEnabled = v)));

        var freqBox = new ComboBox { MinWidth = 130 };
        foreach (var (label, val) in new[] { (Services.Loc.T("settings.freq.daily"), "daily"), (Services.Loc.T("settings.freq.weekly"), "weekly"), (Services.Loc.T("settings.freq.monthly"), "monthly") })
        {
            var item = new ComboBoxItem { Content = label, Tag = val };
            freqBox.Items.Add(item);
            if (_s.AutoCleanFrequency == val) item.IsSelected = true;
        }
        freqBox.SelectionChanged += (s, e) =>
        {
            if (freqBox.SelectedItem is ComboBoxItem it) { _s.AutoCleanFrequency = (string)it.Tag!; Save(); }
        };
        CleanSection.Children.Add(Row(Services.Loc.T("settings.row.autoclean.freq"), Services.Loc.T("settings.row.autoclean.freq.d"), freqBox));

        // —— 排除目录与白名单 ——
        RenderExclusions();

        // —— 自定义清理目录 ——
        RenderCustomClean();

        // —— 通知 ——
        NotifySection.Children.Add(Row(Services.Loc.T("settings.row.notifydisk"), Services.Loc.T("settings.row.notifydisk.d"),
            Toggle(_s.NotifyDiskLow, v => _s.NotifyDiskLow = v)));
        NotifySection.Children.Add(Row(Services.Loc.T("settings.row.notifyauto"), "",
            Toggle(_s.NotifyAutoCleanResult, v => _s.NotifyAutoCleanResult = v)));
        NotifySection.Children.Add(Row(Services.Loc.T("settings.row.notifyweekly"), "",
            Toggle(_s.NotifyWeeklyReport, v => _s.NotifyWeeklyReport = v)));
        NotifySection.Children.Add(Row(Services.Loc.T("settings.row.notifynews"), Services.Loc.T("settings.row.notifynews.d"),
            Toggle(_s.NotifyProductNews, v => _s.NotifyProductNews = v)));

        // —— 高级 ——
        var threadsBox = new ComboBox { MinWidth = 130 };
        for (int i = 2; i <= Math.Max(8, Environment.ProcessorCount); i += 2)
        {
            var item = new ComboBoxItem { Content = i + " " + Services.Loc.T("settings.threads"), Tag = i };
            threadsBox.Items.Add(item);
            if (_s.ScanThreads == i) item.IsSelected = true;
        }
        threadsBox.SelectionChanged += (s, e) =>
        {
            if (threadsBox.SelectedItem is ComboBoxItem it) { _s.ScanThreads = (int)it.Tag!; Save(); }
        };
        AdvancedSection.Children.Add(Row(Services.Loc.T("settings.row.threads"), Services.Loc.T("settings.row.threads.d"), threadsBox));
        AdvancedSection.Children.Add(Row(Services.Loc.T("settings.row.hidden"), "",
            Toggle(_s.ScanHiddenFiles, v => _s.ScanHiddenFiles = v)));
        var links = Toggle(false, _ => { });
        links.IsEnabled = false;
        AdvancedSection.Children.Add(Row(Services.Loc.T("settings.row.junction"), Services.Loc.T("settings.row.junction.d"), links));

        // 排障:打开数据与日志文件夹
        var logBtn = new Button
        {
            Content = Services.Loc.T("settings.btn.openlog"),
            Style = (Style)FindResource("SmallSecondaryButton"),
        };
        logBtn.Click += (s, e) =>
        {
            try { CleanOptimize.Core.Utils.ShellUtil.OpenPath(CleanOptimize.Core.Store.SettingsService.DataDir); }
            catch (Exception ex) { _main.ShowToast(Services.Loc.T("tool.win.openfail") + ex.Message, ToastType.Warning); }
        };
        AdvancedSection.Children.Add(Row(Services.Loc.T("settings.row.openlog"), Services.Loc.T("settings.row.openlog.d"), logBtn));

        // —— 隐私 ——
        var statsToggle = Toggle(false, _ => { });
        statsToggle.IsEnabled = false;
        var crashToggle = Toggle(false, _ => { });
        crashToggle.IsEnabled = false;
        PrivacySection.Children.Add(Row(Services.Loc.T("settings.row.stats"), UpdateService.OfflineMessage, statsToggle));
        PrivacySection.Children.Add(Row(Services.Loc.T("settings.row.crash"), UpdateService.OfflineMessage, crashToggle));
    }

    private void RenderExclusions()
    {
        ExclusionList.Children.Clear();
        void AddEntryRow(string path, bool isWhitelist)
        {
            var row = new Border
            {
                Background = (Brush)FindResource("ItemHoverBrush"),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 7, 10, 7),
                Margin = new Thickness(0, 4, 0, 0),
            };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var chip = new Border
            {
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(7, 2, 7, 3),
                VerticalAlignment = VerticalAlignment.Center,
                Background = isWhitelist
                    ? (Brush)FindResource("SuccessSoftBrush")
                    : (Brush)FindResource("PrimarySoftBrush"),
            };
            chip.Child = new TextBlock
            {
                Text = isWhitelist ? Services.Loc.T("settings.whitelist") : Services.Loc.T("settings.excluded"),
                FontSize = 10.5,
                FontWeight = FontWeights.Bold,
                Foreground = isWhitelist
                    ? (Brush)FindResource("SuccessBrush")
                    : (Brush)FindResource("PrimaryBrush"),
            };
            Grid.SetColumn(chip, 0);

            var pathText = new TextBlock
            {
                Text = path,
                FontSize = 12,
                Foreground = (Brush)FindResource("TextPrimaryBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 8, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
            Grid.SetColumn(pathText, 1);

            var del = new Button
            {
                Content = Services.Loc.T("btn.remove"),
                Style = (Style)FindResource("LinkButton"),
                Foreground = (Brush)FindResource("DangerBrush"),
            };
            del.Click += (s, e) =>
            {
                if (isWhitelist) _s.Whitelist.Remove(path);
                else _s.ExcludedPaths.Remove(path);
                Save();
                RenderExclusions();
            };
            Grid.SetColumn(del, 2);

            grid.Children.Add(chip);
            grid.Children.Add(pathText);
            grid.Children.Add(del);
            row.Child = grid;
            ExclusionList.Children.Add(row);
        }

        foreach (var p in _s.ExcludedPaths) AddEntryRow(p, isWhitelist: false);
        foreach (var p in _s.Whitelist) AddEntryRow(p, isWhitelist: true);
        if (_s.ExcludedPaths.Count == 0 && _s.Whitelist.Count == 0)
        {
            ExclusionList.Children.Add(new TextBlock
            {
                Text = Services.Loc.T("settings.exc.empty"),
                FontSize = 12,
                Foreground = (Brush)FindResource("TextTertiaryBrush"),
            });
        }
    }

    private void BtnAddExclude_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new System.Windows.Forms.FolderBrowserDialog { Description = Services.Loc.T("settings.pick.exclude") };
        if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        if (!_s.ExcludedPaths.Contains(dlg.SelectedPath))
        {
            _s.ExcludedPaths.Add(dlg.SelectedPath);
            Save();
            RenderExclusions();
        }
    }

    private void BtnAddWhitelist_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Title = Services.Loc.T("settings.pick.whitelist") };
        if (dlg.ShowDialog() != true) return;
        if (!_s.Whitelist.Contains(dlg.FileName))
        {
            _s.Whitelist.Add(dlg.FileName);
            Save();
            RenderExclusions();
        }
    }

    // ————— 自定义清理目录 —————

    private void RenderCustomClean()
    {
        CustomCleanList.Children.Clear();
        foreach (var p in _s.CustomCleanDirs)
        {
            var row = new Border
            {
                Background = (Brush)FindResource("ItemHoverBrush"),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 7, 10, 7),
                Margin = new Thickness(0, 4, 0, 0),
            };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var pathText = new TextBlock
            {
                Text = p,
                FontSize = 12,
                Foreground = (Brush)FindResource("TextPrimaryBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                ToolTip = p,
            };
            Grid.SetColumn(pathText, 0);
            var del = new Button
            {
                Content = Services.Loc.T("btn.remove"),
                Style = (Style)FindResource("LinkButton"),
                Foreground = (Brush)FindResource("DangerBrush"),
            };
            var captured = p;
            del.Click += (s, e) =>
            {
                _s.CustomCleanDirs.Remove(captured);
                Save();
                RenderCustomClean();
            };
            Grid.SetColumn(del, 1);
            grid.Children.Add(pathText);
            grid.Children.Add(del);
            row.Child = grid;
            CustomCleanList.Children.Add(row);
        }
        if (_s.CustomCleanDirs.Count == 0)
        {
            CustomCleanList.Children.Add(new TextBlock
            {
                Text = Services.Loc.T("settings.custom.empty"),
                FontSize = 12,
                Foreground = (Brush)FindResource("TextTertiaryBrush"),
            });
        }
    }

    private void BtnAddCustomClean_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new System.Windows.Forms.FolderBrowserDialog { Description = Services.Loc.T("settings.custom.pick") };
        if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        // 用户数据目录受 SafetyGuard 保护,清理会被拦截——提前提示避免误导
        var deny = CleanOptimize.Core.Security.SafetyGuard.FindDenyReason(dlg.SelectedPath);
        if (deny != null)
        {
            _main.ShowToast(Services.Loc.T("settings.custom.protected"), ToastType.Warning);
            return;
        }
        if (!_s.CustomCleanDirs.Contains(dlg.SelectedPath))
        {
            _s.CustomCleanDirs.Add(dlg.SelectedPath);
            Save();
            RenderCustomClean();
        }
    }

    private void BtnClearLocal_Click(object sender, RoutedEventArgs e)
    {
        if (!Services.DialogService.Confirm(_main, Services.Loc.T("history.clear.title"), Services.Loc.T("history.clear.msg"), Services.Loc.T("btn.clearhistory")))
            return;
        HistoryService.Clear();
        _main.ShowToast(Services.Loc.T("toast.historycleared"));
    }

    // ————— 检查更新 —————

    private void BtnCheckUpdate_Click(object sender, RoutedEventArgs e)
    {
        UpdateStateText.Text = UpdateService.OfflineMessage;
        _main.ShowToast(UpdateService.OfflineMessage, ToastType.Info);
    }
}
