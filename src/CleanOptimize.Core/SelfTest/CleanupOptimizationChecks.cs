using System.Text.Json;
using System.Text.Json.Nodes;
using CleanOptimize.Core.Models;
using CleanOptimize.Core.Rules;
using CleanOptimize.Core.Store;
using CleanOptimize.Core.Utils;

namespace CleanOptimize.Core.SelfTest;

internal static class CleanupOptimizationChecks
{
    public static void Run(Action<string, bool, string> report)
    {
        void Check(string name, Action action)
        {
            try { using var fixture = new Fixture(); action(); report(name, true, ""); }
            catch (Exception ex) { report(name, false, ex.ToString()); }
        }
        Check("紧凑扫描结果跨块、路径前缀上限及虚拟路径完整还原", () =>
        {
            var items = new CleanItemCollection();
            for (int i = 0; i < 5200; i++) items.Add(new CleanItem
            {
                Path = $"C:\\cache\\目录{i % 4500}\\条目{i}.tmp", Size = i, Note = "note" + i,
                IsDirectory = i % 2 == 0, Risk = RiskLevel.Low, ModifiedTime = new DateTime(2026, 1, 1).AddSeconds(i),
            });
            items.Add(new CleanItem { Path = "reg://Software/Test", Size = 0 });
            for (int i = 0; i < 5200; i++)
            {
                var item = items[i];
                Require(item.Path == $"C:\\cache\\目录{i % 4500}\\条目{i}.tmp" && item.Size == i && item.Note == "note" + i &&
                    item.IsDirectory == (i % 2 == 0) && item.Risk == RiskLevel.Low && item.ModifiedTime.Second == i % 60);
            }
            Require(items.Count == 5201 && items[5200].Path == "reg://Software/Test");
            items[0].Path = "changed";
            Require(items[0].Path != "changed");
        });
        Check("流式扫描在单目录枚举期间响应取消并关闭枚举器", () =>
        {
            var files = MakeFiles(128);
            using var cancel = new CancellationTokenSource();
            using var iterator = ProbeRule.Walk(Files, new ScanContext { Ct = cancel.Token }).GetEnumerator();
            Require(iterator.MoveNext());
            cancel.Cancel();
            try { iterator.MoveNext(); throw new InvalidOperationException("未取消"); }
            catch (OperationCanceledException) { }
            Require(files.All(i => File.Exists(i.Path)));
        });
        Check("实际批量隔离与永久删除分开统计且保持恢复内容", () =>
        {
            var items = MakeFiles(130);
            var q = QuarantineService.Instance;
            _ = q.Entries;
            var writes = q.ManifestWriteCount;
            var summary = RunClean(new ProbeRule(Files), items);
            Require(summary.QuarantineCount == 130 && summary.FreedBytes == 0 && summary.QuarantinedBytes == 8515 && summary.RemovedBytes == 8515);
            Require(q.ManifestWriteCount - writes == 6 && items.All(i => !File.Exists(i.Path)));
            var restarted = new QuarantineService();
            Require(restarted.Entries.Count == 130 && restarted.TotalBytes == 8515);
            var first = restarted.Entries.First();
            Require(restarted.Restore(first.Id) == null && File.ReadAllBytes(first.OriginalPath).All(b => b == 1));
            Require(HistoryService.LoadAll().First(h => h.Type == "Clean").Bytes == 0);
            Require(restarted.PurgeAll() == 129);
        });
        Check("永久删除按执行时文件长度统计且保留占用文件", () =>
        {
            var items = MakeFiles(2);
            File.WriteAllBytes(items[0].Path, new byte[11]);
            SettingsService.Current.QuarantineEnabled = false;
            using var held = new FileStream(items[1].Path, FileMode.Open, FileAccess.Read, FileShare.None);
            var summary = RunClean(new ProbeRule(Files), items);
            Require(summary.SuccessCount == 1 && summary.SkipCount == 1 && summary.FreedBytes == 11 && File.Exists(items[1].Path));
        });
        Check("目录部分删除只统计成功删除的字节", () =>
        {
            var items = MakeFiles(2);
            SettingsService.Current.QuarantineEnabled = false;
            using var held = new FileStream(items[1].Path, FileMode.Open, FileAccess.Read, FileShare.None);
            var summary = RunClean(new ProbeRule(SettingsService.DataDir), new[] { new CleanItem { Path = Files, Size = 999, IsDirectory = true } });
            Require(summary.FailCount == 1 && summary.FreedBytes == 1 && File.Exists(items[1].Path) && !File.Exists(items[0].Path));
        });
        Check("两万项清理明细有界、异常剩余项计数和进度终值准确", () =>
        {
            var items = Enumerable.Range(0, 20000).Select(i => new CleanItem { Path = "virtual-" + i, Size = 1 }).ToArray();
            var updates = new List<CleanProgress>();
            var summary = RunClean(new SimulatedRule(null), items, progress: new InlineProgress<CleanProgress>(updates.Add));
            Require(summary.SuccessCount == 5000 && summary.FailCount == 15000 && summary.FreedBytes == 5000);
            Require(summary.Details.Count == 1000 && summary.OmittedDetailCount == 19000);
            Require(updates.Count < 100 && updates.Last().Processed == 20000 && updates.Last().Total == 20000 && updates.Last().FreedBytes == 5000);
            Require(updates.Zip(updates.Skip(1)).All(p => p.First.Processed <= p.Second.Processed));
        });
        Check("取消清理仍发送实际进度且不会虚报百分之百", () =>
        {
            using var cancel = new CancellationTokenSource();
            var updates = new List<CleanProgress>();
            var items = Enumerable.Range(0, 20000).Select(i => new CleanItem { Path = "virtual-" + i, Size = 1 }).ToArray();
            var summary = RunClean(new SimulatedRule(cancel), items, cancel.Token, new InlineProgress<CleanProgress>(updates.Add));
            Require(summary.Cancelled && summary.SuccessCount == 123 && summary.FreedBytes == 123);
            Require(updates.Last().Processed == 123 && updates.Last().Total == 20000 && updates.Last().FreedBytes == 123);
        });
        Check("批次预写失败时所有源文件保持原位", () =>
        {
            var items = MakeFiles(8);
            var q = new QuarantineService(); q.PurgeAll();
            int successes = 0, failures = 0;
            using (var held = new FileStream(Manifest, FileMode.Open, FileAccess.Read, FileShare.None))
                q.MoveBatchIntoQuarantine(items, "test", CancellationToken.None, null,
                    (_, moved, _) => { if (moved) successes++; else failures++; });
            Require(successes == 0 && failures == 8 && items.All(i => File.Exists(i.Path)) && new QuarantineService().Entries.Count == 0);
        });
        Check("移动前整批恢复记录已落盘", () =>
        {
            var items = MakeFiles(4);
            var q = new QuarantineService(); int count = 0;
            q.MoveBatchIntoQuarantine(items, "test", CancellationToken.None, null, (_, moved, _) =>
            {
                Require(moved);
                if (++count == 1)
                {
                    var intents = JsonSerializer.Deserialize<List<QuarantineEntry>>(File.ReadAllText(Manifest))!;
                    Require(intents.Count == 4 && intents.All(e => e.Pending));
                }
            });
            Require(new QuarantineService().Entries.Count == 4 && q.PurgeAll() == 4);
        });
        Check("取消加最终清单写入失败后重启仍可恢复已移动文件", () =>
        {
            var items = MakeFiles(3);
            using var cancel = new CancellationTokenSource();
            FileStream? held = null;
            try
            {
                var q = new QuarantineService();
                try
                {
                    q.MoveBatchIntoQuarantine(items, "test", cancel.Token, null, (_, moved, _) =>
                    {
                        Require(moved);
                        held = new FileStream(Manifest, FileMode.Open, FileAccess.Read, FileShare.Read);
                        cancel.Cancel();
                    });
                    throw new InvalidOperationException("未取消批次");
                }
                catch (OperationCanceledException) { }
            }
            finally { held?.Dispose(); }
            var restarted = new QuarantineService();
            Require(restarted.Entries.Count == 1 && !File.Exists(items[0].Path) && File.Exists(items[1].Path) && File.Exists(items[2].Path));
            Require(restarted.Restore(restarted.Entries[0].Id) == null && File.ReadAllBytes(items[0].Path).SequenceEqual(new byte[] { 1 }));
        });
        Check("旧格式隔离清单继续加载和恢复", () =>
        {
            var item = MakeFiles(1)[0];
            var q = new QuarantineService(); Require(q.MoveIntoQuarantine(item.Path, "test", out _));
            var json = JsonNode.Parse(File.ReadAllText(Manifest))!.AsArray();
            json[0]!.AsObject().Remove("Pending"); File.WriteAllText(Manifest, json.ToJsonString());
            var restarted = new QuarantineService();
            Require(restarted.Entries.Count == 1 && restarted.Restore(restarted.Entries[0].Id) == null && File.Exists(item.Path));
        });
        Check("损坏的隔离清单拒绝后续移动并保留源文件", () =>
        {
            var item = MakeFiles(1)[0]; File.WriteAllText(Manifest, "broken");
            Require(!new QuarantineService().MoveIntoQuarantine(item.Path, "test", out var error) && !string.IsNullOrWhiteSpace(error) && File.Exists(item.Path));
        });
    }

    private static string Files => Path.Combine(SettingsService.DataDir, "files");
    private static string Manifest => Path.Combine(SettingsService.DataDir, "quarantine.json");
    private static void Require(bool condition) { if (!condition) throw new InvalidOperationException("验收条件不满足"); }
    private static List<CleanItem> MakeFiles(int count)
    {
        Directory.CreateDirectory(Files);
        return Enumerable.Range(1, count).Select(i =>
        {
            var path = Path.Combine(Files, $"sample-{i:D4}.tmp");
            File.WriteAllBytes(path, Enumerable.Repeat((byte)(i % 256), i).ToArray());
            return new CleanItem { Path = path, Size = i };
        }).ToList();
    }
    private static CleanSummary RunClean(IScanRule rule, IReadOnlyList<CleanItem> items, CancellationToken token = default, IProgress<CleanProgress>? progress = null)
        => new CleanService().CleanAsync(new[] { new CleanRequestRule { Rule = rule, Result = new RuleResult(), Selected = items } }, token, progress).GetAwaiter().GetResult();
    private sealed class Fixture : IDisposable
    {
        private readonly string _previous = SettingsService.DataDir;
        private readonly AppSettings _settings = SettingsService.Current;
        private readonly string _root;
        public Fixture()
        {
            _root = Path.Combine(_previous, "Optimization_" + Guid.NewGuid().ToString("N"));
            SettingsService.InitializeSelfTest(_root);
        }
        public void Dispose() { SettingsService.InitializeSelfTest(_previous, _settings); FileUtil.DeleteDirectoryTree(_root); }
    }
    private sealed class ProbeRule(string root) : RuleBase
    {
        public override string Id => "optimization_probe";
        public override string Name => Id;
        public override RiskLevel Risk => RiskLevel.Safe;
        public override RuleGroup Group => RuleGroup.SystemJunk;
        public override IEnumerable<string> AllowedRoots() => new[] { root };
        public override Task<RuleResult> ScanAsync(ScanContext ctx) => Task.FromResult(new RuleResult());
        public static IEnumerable<string> Walk(string root, ScanContext ctx) => SafeWalk(root, ctx);
    }
    private sealed class SimulatedRule(CancellationTokenSource? cancel) : RuleBase
    {
        public override string Id => "simulated_outcomes";
        public override string Name => Id;
        public override RiskLevel Risk => RiskLevel.Safe;
        public override RuleGroup Group => RuleGroup.SystemJunk;
        public override Task<RuleResult> ScanAsync(ScanContext ctx) => Task.FromResult(new RuleResult());
        public override Task CleanAsync(CleanContext ctx)
        {
            int processed = 0;
            foreach (var item in ctx.Selected)
            {
                ctx.Ct.ThrowIfCancellationRequested();
                ctx.Report(item, CleanItemStatus.Success, "simulated");
                ctx.Report(item, CleanItemStatus.Success, "duplicate callback");
                if (++processed == 123) cancel?.Cancel();
                if (cancel == null && processed == 5000) throw new IOException("simulated failure");
            }
            return Task.CompletedTask;
        }
    }
    private sealed class InlineProgress<T>(Action<T> action) : IProgress<T> { public void Report(T value) => action(value); }
}
