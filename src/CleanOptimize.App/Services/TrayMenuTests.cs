using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using CleanOptimize.App.Controls;
using CleanOptimize.Core.Store;

namespace CleanOptimize.App.Services;

internal static class TrayMenuTests
{
    internal static void Verify()
    {
        var main = new MainWindow();
        using var service = new NotifyIconService(main);
        var language = Loc.Current;
        bool automatic = SettingsService.Current.AutoCleanEnabled;
        var build = typeof(NotifyIconService).GetMethod("BuildMenu", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var field = typeof(NotifyIconService).GetField("_menu", BindingFlags.Instance | BindingFlags.NonPublic)!;
        TrayMenu? previous = null;
        try
        {
            foreach (var theme in new[] { "light", "dark" })
            foreach (var (code, _) in Loc.Supported)
            {
                ThemeManager.Apply(theme); Loc.SetLanguage(code);
                build.Invoke(service, null);
                if (previous != null && !previous.IsDisposed) throw new Exception("Old tray menu retained after rebuilding");
                var menu = (TrayMenu)field.GetValue(service)!;
                previous = menu;
                var actions = menu.Items.OfType<TrayMenuItem>().ToArray();
                if (!actions.Select(i => i.Action).SequenceEqual(Enum.GetValues<TrayAction>()) || actions[0].Enabled)
                    throw new Exception("Tray actions changed or heading is actionable");
                var toggle = actions.Single(i => i.Action == TrayAction.AutoClean);
                SettingsService.Current.AutoCleanEnabled = false;
                typeof(TrayMenu).GetMethod("OnOpening", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .Invoke(menu, [new CancelEventArgs()]);
                if (toggle.Checked || toggle.Text != NotifyIconService.AutoCleanText()) throw new Exception("Tray uses stale settings");
                toggle.PerformClick();
                if (!toggle.Checked || !SettingsService.Current.AutoCleanEnabled) throw new Exception("Tray toggle failed");
                menu.PerformLayout();
                menu.Size = menu.GetPreferredSize(Size.Empty);
                menu.PerformLayout();
                foreach (var item in actions)
                {
                    float scale = menu.DeviceDpi / 96f;
                    int labelWidth = TextRenderer.MeasureText(item.Text, item.Font, Size.Empty,
                        TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding).Width;
                    int inset = (int)((item.Action == TrayAction.AutoClean ? 91 : 55) * scale);
                    if (item.Width < labelWidth + inset || item.Bounds.Right > menu.Width || item.Bounds.Bottom > menu.Height || item.Height < 30 * scale)
                        throw new Exception($"Tray text/action clipped: {code}/{theme}/{item.Text}, {item.Bounds}, menu {menu.Size}, text {labelWidth}");
                }
                actions[1].Select();
                using var bitmap = new Bitmap(menu.Width, menu.Height);
                menu.DrawToBitmap(bitmap, new Rectangle(System.Drawing.Point.Empty, bitmap.Size));
                if (bitmap.GetPixel(3, 3).ToArgb() != menu.BackColor.ToArgb()) throw new Exception("Tray background differs from theme");
                var output = Environment.GetEnvironmentVariable("CM_UI_SNAPSHOT_DIR");
                if (code == "zh-CN" && !string.IsNullOrWhiteSpace(output))
                {
                    if (!Core.Utils.PathUtil.IsLocalPath(output) || Core.Utils.PathUtil.HasReparseAncestor(output)) throw new IOException("Invalid snapshot path");
                    Directory.CreateDirectory(output);
                    bitmap.Save(Path.Combine(output, "tray-" + theme + ".png"), ImageFormat.Png);
                }
                var background = menu.BackColor;
                ThemeManager.Apply(theme == "light" ? "dark" : "light"); menu.ApplyTheme();
                if (!SystemInformation.HighContrast && background == menu.BackColor) throw new Exception("Tray theme failed to update");
                toggle.PerformClick();
                if (toggle.Checked || SettingsService.Current.AutoCleanEnabled) throw new Exception("Tray toggle off failed");
            }
            service.Dispose();
            if (previous is not { IsDisposed: true }) throw new Exception("Tray not disposed");
        }
        finally
        {
            SettingsService.Current.AutoCleanEnabled = automatic; SettingsService.Save();
            main.ForceClose(); Loc.SetLanguage(language); ThemeManager.ApplyFromSettings();
        }
    }
}
