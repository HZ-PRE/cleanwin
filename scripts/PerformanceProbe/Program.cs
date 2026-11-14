using System.Diagnostics;
using System.Text.Json;
using CleanOptimize.Core.Models;
using CleanOptimize.Core.Rules;
using CleanOptimize.Core.SelfTest;
using CleanOptimize.Core.Store;
using CleanOptimize.Core.Utils;

var owned = SelfTest.CreateIsolatedEnvironment();
try
{
    var root = Path.Combine(owned, "fixture-with-a-long-directory-name-for-realistic-cache-paths");
    Directory.CreateDirectory(root);
    for (int i = 0; i < 12000; i++) File.WriteAllBytes(Path.Combine(root, $"cache-{i:D6}.tmp"), new byte[64]);
    var rule = new ProbeRule(root);
    var before = GC.GetTotalMemory(true); // Measurement only, never part of application cleanup.
    var allocation = GC.GetTotalAllocatedBytes(true);
    var watch = Stopwatch.StartNew();
    var result = await rule.ScanAsync(new ScanContext { SkipRecentMinutes = 0 });
    watch.Stop();
    var scanMs = watch.Elapsed.TotalMilliseconds;
    var scanAllocated = GC.GetTotalAllocatedBytes(true) - allocation;
    var scanRetained = GC.GetTotalMemory(true) - before;
    int notifications = 0;
    var selected = result.Items.Take(512).ToList();
    long manifestBytes = 0;
    var manifest = Path.Combine(SettingsService.DataDir, "quarantine.json");
    var progress = new InlineProgress<CleanProgress>(p =>
    {
        notifications++;
        if (p.Processed == 0 && File.Exists(manifest)) manifestBytes += new FileInfo(manifest).Length;
    });
    allocation = GC.GetTotalAllocatedBytes(true);
    watch.Restart();
    var summary = await new CleanService().CleanAsync(new[] { new CleanRequestRule
        { Rule = rule, Result = result, Selected = selected } }, CancellationToken.None, progress);
    watch.Stop();
    var writeMetric = typeof(QuarantineService).GetProperty("ManifestBytesWritten", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
    var writeCount = typeof(QuarantineService).GetProperty("ManifestWriteCount", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
    Console.WriteLine(JsonSerializer.Serialize(new
    {
        ScanFiles = result.Items.Count, ScanBytes = result.TotalSize, ScanMs = scanMs, ScanAllocatedBytes = scanAllocated,
        ScanRetainedBytes = scanRetained, CleanMs = watch.Elapsed.TotalMilliseconds,
        CleanAllocatedBytes = GC.GetTotalAllocatedBytes(true) - allocation, ProgressNotifications = notifications,
        ManifestBytesWritten = writeMetric?.GetValue(QuarantineService.Instance),
        ManifestWriteCount = writeCount?.GetValue(QuarantineService.Instance),
        // On the old implementation there is one per-item notification, plus a duplicate every 20 items.
        OldManifestBytesSampled = manifestBytes, summary.QuarantineCount, summary.FreedBytes
    }));
    if (result.Items.Count != 12000 || result.TotalSize != 768000 || summary.QuarantineCount != 512 || summary.FailCount != 0)
        throw new InvalidOperationException("Performance fixture outcome mismatch");
    GC.KeepAlive(result);
}
finally { FileUtil.DeleteDirectoryTree(owned); }

sealed class ProbeRule(string root) : RuleBase
{
    public override string Id => "performance_fixture";
    public override string Name => "Performance fixture";
    public override RiskLevel Risk => RiskLevel.Safe;
    public override RuleGroup Group => RuleGroup.SystemJunk;
    public override IEnumerable<string> AllowedRoots() => new[] { root };
    public override Task<RuleResult> ScanAsync(ScanContext context)
    {
        var result = new RuleResult(); AddDirFiles(result, context, root, "*", true); return Task.FromResult(result);
    }
}
sealed class InlineProgress<T>(Action<T> action) : IProgress<T> { public void Report(T value) => action(value); }
