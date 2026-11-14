using System.IO;
using System.Windows;
using CleanOptimize.App.Pages;
using CleanOptimize.Core.Models;
using CleanOptimize.Core.Rules;
using CleanOptimize.Core.Store;

namespace CleanOptimize.App.Services;

internal static class CleanupFeatureTests
{
    internal static async Task QuickCleanup()
    {
        var catalog = new RuleCatalog();
        var safe = QuickCleanPlan.SelectRules(catalog.Rules, new QuickCleanRequest());
        if (safe.Count == 0 || safe.Any(r => r.Risk > RiskLevel.Low || !r.DefaultSelected || r.Group is RuleGroup.Privacy or RuleGroup.RecycleBin))
            throw new Exception("Quick cleanup selected unsafe rules");
        if (QuickCleanPlan.SelectRules(catalog.Rules, new QuickCleanRequest("update_cache")).Count != 0 ||
            QuickCleanPlan.SelectRules(catalog.Rules, new QuickCleanRequest("missing")).Count != 0)
            throw new Exception("Quick cleanup broadened requested rule");
        var settings = SettingsService.Current;
        bool quarantine = settings.QuarantineEnabled;
        int recent = settings.SkipRecentMinutes;
        var root = Path.Combine(SettingsService.DataDir, "quick-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var clean = Path.Combine(root, "clean.tmp");
        var keep = Path.Combine(root, "keep.tmp");
        File.WriteAllText(clean, "123"); File.WriteAllText(keep, "keep");
        settings.Whitelist.Add(keep); settings.QuarantineEnabled = false; settings.SkipRecentMinutes = 0;
        var main = new MainWindow();
        try
        {
            var rule = new SandboxRule(root);
            catalog.Rules.Clear(); catalog.Rules.Add(rule);
            var scanner = new ScannerService(catalog);
            var page = new SmartScanPage(main);
            int confirmations = 0;
            ScanCache.Save(new LastScanSummary { TotalBytes = 12345 });
            await page.ScanAsync(new QuickCleanRequest(rule.Id), scanner, message => { confirmations++; return false; });
            if (!File.Exists(clean) || !File.Exists(keep) || confirmations != 1 || page.OperationActive || ScanCache.Load()?.TotalBytes != 12345)
                throw new Exception("Cancelling quick cleanup changed files/cache or left operation active");
            await page.ScanAsync(new QuickCleanRequest(rule.Id), scanner, message => { confirmations++; return true; });
            if (File.Exists(clean) || File.ReadAllText(keep) != "keep" || confirmations != 2 || page.OperationActive || rule.Scans != 2)
                throw new Exception("Quick cleanup failed or started redundant scans");
            if (ScanCache.Load() != null) throw new Exception("Quick cleanup retained stale full-scan cache");
            if (((FrameworkElement)page.FindName("PanelDone")).Visibility != Visibility.Visible)
                throw new Exception("Quick cleanup did not display result");
            File.WriteAllText(clean, "cancel");
            rule.WaitForCancellation = true;
            var scanning = page.ScanAsync(new QuickCleanRequest(rule.Id), scanner, _ => throw new Exception("Cancelled scan prompted cleanup"));
            page.TryCancelCurrent();
            await scanning;
            if (!File.Exists(clean) || page.OperationActive) throw new Exception("Quick scan cancellation failed");
        }
        finally
        {
            main.ForceClose(); settings.Whitelist.Remove(keep); settings.QuarantineEnabled = quarantine; settings.SkipRecentMinutes = recent;
            Directory.Delete(root, recursive: true);
        }
    }

    internal static async Task AutoCleanNotifications()
    {
        var settings = SettingsService.Current;
        bool notify = settings.NotifyAutoCleanResult;
        try
        {
            var expected = new CleanSummary { FreedBytes = 1024, QuarantinedBytes = 4096, SuccessCount = 1, QuarantineCount = 2, SkipCount = 3, FailCount = 4 };
            var scheduler = new AutoCleanScheduler(_ => Task.FromResult(expected));
            int notices = 0;
            scheduler.Completed += result => { if (!ReferenceEquals(result, expected)) throw new Exception("Incorrect cleanup result"); notices++; };
            settings.NotifyAutoCleanResult = false;
            await scheduler.RunOnceAsync();
            if (notices != 0) throw new Exception("Disabled cleanup notification emitted");
            settings.NotifyAutoCleanResult = true;
            await scheduler.RunOnceAsync();
            if (notices != 1 || scheduler.IsRunning) throw new Exception("Cleanup notification missing or duplicate");
            foreach (var (language, _) in Loc.Supported)
            {
                Loc.SetLanguage(language);
                var message = NotifyIconService.AutoCleanMessage(expected);
                if (!message.Contains(Core.Utils.SizeText.OfBytes(1024)) || !message.Contains(Core.Utils.SizeText.OfBytes(4096)))
                    throw new Exception("Deleted/quarantined bytes mixed in notification");
            }
            var pending = new TaskCompletionSource<CleanSummary>(TaskCreationOptions.RunContinuationsAsynchronously);
            int executions = 0;
            scheduler = new AutoCleanScheduler(token => { executions++; return pending.Task.WaitAsync(token); });
            scheduler.Completed += _ => throw new Exception("Cancelled cleanup notified success");
            var running = scheduler.RunOnceAsync();
            await scheduler.RunOnceAsync();
            if (executions != 1) throw new Exception("Automatic cleanup ran concurrently");
            scheduler.Stop(); await running;
            if (scheduler.IsRunning) throw new Exception("Stopped cleanup retained operation state");
            int failures = 0;
            scheduler = new AutoCleanScheduler(_ => throw new IOException("Simulated cleanup failure"));
            scheduler.Completed += result => { if (result.FailCount > 0 && result.FreedBytes == 0) failures++; };
            await scheduler.RunOnceAsync();
            if (failures != 1) throw new Exception("Cleanup failure fabricated success");
            int empty = 0;
            scheduler = new AutoCleanScheduler(_ => Task.FromResult(new CleanSummary()));
            scheduler.Completed += result => { if (result.FreedBytes == 0 && result.SuccessCount == 0) empty++; };
            await scheduler.RunOnceAsync();
            if (empty != 1) throw new Exception("Empty cleanup result was not reported");
        }
        finally { settings.NotifyAutoCleanResult = notify; Loc.SetLanguage("zh-CN"); }
    }

    private sealed class SandboxRule(string root) : RuleBase
    {
        public override string Id => "quick_fixture";
        public override string Name => "Quick fixture";
        public override RuleGroup Group => RuleGroup.SystemJunk;
        public override RiskLevel Risk => RiskLevel.Safe;
        public int Scans { get; private set; }
        public bool WaitForCancellation { get; set; }
        public override IEnumerable<string> AllowedRoots() => [root];
        public override async Task<RuleResult> ScanAsync(ScanContext context)
        {
            Scans++;
            if (WaitForCancellation) await Task.Delay(Timeout.Infinite, context.Ct);
            var result = new RuleResult { RuleId = Id, Name = Name, Group = Group, Risk = Risk, CanRestore = true };
            AddDirFiles(result, context, root, "*.tmp", recursive: false);
            return result;
        }
    }
}
