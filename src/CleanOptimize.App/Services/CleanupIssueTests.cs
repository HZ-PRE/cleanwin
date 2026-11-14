using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CleanOptimize.App.Controls;
using CleanOptimize.App.Pages;
using CleanOptimize.Core.Analysis;
using CleanOptimize.Core.Models;

namespace CleanOptimize.App.Services;

internal static class CleanupIssueTests
{
    internal static void FailureDetails()
    {
        var main = new MainWindow();
        string language = Loc.Current;
        try
        {
            foreach (var theme in new[] { "light", "dark" })
            foreach (var (code, _) in Loc.Supported)
            {
                ThemeManager.Apply(theme); Loc.SetLanguage(code);
                var summary = new CleanSummary { FailCount = 1001, SkipCount = 1 };
                for (int i = 0; i < 1000; i++) summary.Details.Add(new CleanItemOutcome
                {
                    Path = @"C:\Example\Temp\" + i + ".tmp", Status = i == 0 ? CleanItemStatus.SkippedLocked : CleanItemStatus.Failed,
                    Note = Loc.T("clean.reason.denied")
                });
                var report = new CleanupIssueReport(summary);
                Require(report.Total == 1002 && report.Items.Length == 1000 && report.Summary.Contains("1002"), "Incomplete issue count");
                Require(CleanupIssueReport.Reason(report.Items[0]) == Loc.T("clean.reason.locked"), "Locked explanation missing");
                Require(CleanupIssueReport.Reason(new CleanItemOutcome { Status = CleanItemStatus.NotFound }) == Loc.T("clean.reason.missing"), "Missing explanation incorrect");
                Require(CleanupIssueReport.Reason(new CleanItemOutcome { Status = CleanItemStatus.Failed }) == Loc.T("clean.reason.unknown"), "Empty reason not explained");
                var clean = new CleanSpacePage(main);
                var smart = new SmartScanPage(main);
                clean.ApplyCleanOutcome(summary); smart.ApplyCleanOutcome(summary);
                foreach (var page in new Page[] { clean, smart })
                {
                    Require(((Button)page.FindName("BtnCleanupIssues")).Visibility == Visibility.Visible, "Issue action missing");
                    Layout(page, 660, 540);
                }
                Require(((TextBlock)smart.FindName("DoneIssueText")).Text.Contains(Loc.T("clean.reason.locked")), "Completion reason missing");
                var window = new Windows.CleanupIssuesWindow(main, report);
                try
                {
                    var content = (FrameworkElement)window.Content;
                    Layout(content, 500, 320);
                    var list = (VirtualRows)window.FindName("IssueList");
                    Require(list.Items.Count == 1000 && list.ActualHeight > 30, "Issue list has no viewport");
                    int realized = Enumerable.Range(0, 1000).Count(i => list.ItemContainerGenerator.ContainerFromIndex(i) != null);
                    Require(realized is > 0 and < 50, "Issue rows not virtualized: " + realized);
                    list.ScrollIntoView(report.Items[^1]); Layout(content, 500, 320);
                    Require(list.ItemContainerGenerator.ContainerFromIndex(999) != null && list.ItemContainerGenerator.ContainerFromIndex(0) == null, "Issue list cannot recycle rows");
                    if (code == "zh-CN")
                    {
                        list.ScrollIntoView(report.Items[0]); Layout(content, 676, 456);
                        Capture(content, "cleanup-issues-" + theme, 676, 456);
                    }
                }
                finally { window.Close(); }
                Require(((VirtualRows)window.FindName("IssueList")).Items.Count == 0, "Closed report retains items");
                clean.ApplyCleanOutcome(new CleanSummary()); smart.ApplyCleanOutcome(new CleanSummary());
                Require(((Button)clean.FindName("BtnCleanupIssues")).Visibility == Visibility.Collapsed &&
                    ((Button)smart.FindName("BtnCleanupIssues")).Visibility == Visibility.Collapsed, "Old issues retained after success");
            }
        }
        finally { main.ForceClose(); Loc.SetLanguage(language); }
    }

    internal static void LongFileActions()
    {
        var main = new MainWindow();
        string language = Loc.Current;
        try
        {
            foreach (var theme in new[] { "light", "dark" })
            foreach (var (code, _) in Loc.Supported)
            foreach (var size in new[] { (660, 500), (760, 620) })
            {
                ThemeManager.Apply(theme); Loc.SetLanguage(code);
                var page = new SpaceAnalysisPage(main);
                ((FrameworkElement)page.FindName("LargeCard")).Visibility = Visibility.Visible;
                var list = (VirtualRows)page.FindName("LargeList");
                var shortFile = new LargeFileInfo { Path = @"C:\Example\small.zip", Size = 1024 };
                var longFile = new LargeFileInfo { Path = @"C:\Example\" + string.Concat(Enumerable.Repeat("很长的目录路径\\", 80)) + new string('名', 220) + ".zip", Size = 1024 };
                list.SetItems(new[] { longFile, shortFile }); list.SelectedIndex = 0;
                Layout(page, size.Item1, size.Item2);
                var button = (Button)page.FindName("BtnFileRecycle");
                var point = button.TranslatePoint(new System.Windows.Point(), page);
                Require(button.IsEnabled && button.ActualHeight > 20 && point.Y >= 0 && point.Y + button.ActualHeight <= size.Item2 + 1 && point.X + button.ActualWidth <= size.Item1 + 1,
                    $"Recycle button outside page: {code}/{theme}/{size}: {point}, {button.ActualHeight}");
                var scroll = (ScrollViewer)page.FindName("FileDetailScroll");
                Require(scroll.ScrollableHeight > 0 && scroll.ViewportHeight > 0, "Long details cannot scroll");
                Require(((TextBlock)page.FindName("FileDetailMeta")).Text.Contains(longFile.Path), "Full path was lost");
                scroll.ScrollToEnd(); Layout(page, size.Item1, size.Item2);
                Require(button.TranslatePoint(new System.Windows.Point(), page) == point, "Scrolling moved action buttons");
                if (code == "zh-CN" && size.Item1 == 760) Capture(page, "space-long-path-" + theme, size.Item1, size.Item2);
                list.SelectedIndex = 1; Layout(page, size.Item1, size.Item2);
                Require(scroll.VerticalOffset == 0 && ((TextBlock)page.FindName("FileDetailMeta")).ToolTip as string == shortFile.Path, "Selection retained stale path/scroll");
                list.SetItems(null);
                Require(!button.IsEnabled && ((TextBlock)page.FindName("FileDetailMeta")).ToolTip == null, "Empty selection retains target");
            }
        }
        finally { main.ForceClose(); Loc.SetLanguage(language); }
    }

    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Layout(FrameworkElement element, int width, int height)
    {
        element.Measure(new System.Windows.Size(width, height));
        element.Arrange(new Rect(0, 0, width, height)); element.UpdateLayout();
    }
    private static void Capture(FrameworkElement element, string name, int width, int height)
    {
        string? output = Environment.GetEnvironmentVariable("CM_UI_SNAPSHOT_DIR");
        if (string.IsNullOrWhiteSpace(output)) return;
        if (!Core.Utils.PathUtil.IsLocalPath(output) || Core.Utils.PathUtil.HasReparseAncestor(output)) throw new IOException("Invalid snapshot directory");
        Directory.CreateDirectory(output);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(element);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(output, name + ".png")); encoder.Save(file);
    }
}
