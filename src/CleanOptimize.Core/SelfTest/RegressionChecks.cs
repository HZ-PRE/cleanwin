using System.Diagnostics;
using CleanOptimize.Core.Analysis;
using CleanOptimize.Core.Models;
using CleanOptimize.Core.Rules;
using CleanOptimize.Core.Security;
using CleanOptimize.Core.Store;
using CleanOptimize.Core.Utils;

namespace CleanOptimize.Core.SelfTest;

internal static class RegressionChecks
{
    public static void Run(Action<string, bool, string> report)
    {
        void Check(string name, bool ok) => report(name, ok, "");
        var root = Path.Combine(Path.GetTempPath(), "CleanOptimizeRegression_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            CheckDuplicateRecycle(report);
            CheckManualTools(report);
            var iconFixture = Path.Combine(root, "test, icon.ico");
            File.WriteAllBytes(iconFixture, new byte[] { 0 });
            Check("图标路径支持引号、逗号和负资源索引", Apps.AppInventoryService.TryResolveIcon($"\"{iconFixture}\",-12", out var iconPath, out var iconIndex) && iconPath == iconFixture && iconIndex == -12);
            Check("图标支持无引号 ICO 路径", Apps.AppInventoryService.TryResolveIcon(iconFixture + ",0", out iconPath, out iconIndex) && iconPath == iconFixture && iconIndex == 0);
            Check("图标拒绝网络、缺失与相对路径", !Apps.AppInventoryService.TryResolveIcon(@"\\example.invalid\share\icon.ico", out _, out _) &&
                !Apps.AppInventoryService.TryResolveIcon("icon.ico", out _, out _) && !Apps.AppInventoryService.TryResolveIcon(Path.Combine(root, "missing.dll"), out _, out _));
            Check("拒绝删除 Windows 根目录", SafetyGuard.FindDenyReason(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) != null);
            var keep = Path.Combine(root, "keep.txt");
            File.WriteAllText(keep, "keep");
            Check("白名单保护父目录整体删除", SafetyGuard.ValidateForClean(root, new[] { root }, new HashSet<string> { keep }) != null);
            Check("拒绝 UNC 与设备路径", !PathUtil.IsLocalPath(@"\\example.invalid\share\file") && !PathUtil.IsLocalPath(@"\\?\C:\Windows\x"));
            Check("拒绝相对路径和数据流路径", !PathUtil.IsLocalPath("relative.txt") && !PathUtil.IsLocalPath(keep + ":stream"));
            Check("残留识别拒绝路径穿越及共享系统键", !Apps.AppInventoryService.IsSafeLeftoverToken(@"..\Microsoft") &&
                !Apps.AppInventoryService.IsSafeLeftoverToken("Microsoft") && !Apps.AppInventoryService.IsSafeLeftoverToken(@"C:\Windows") &&
                Apps.AppInventoryService.IsSafeLeftoverToken("ExampleApp"));
            Check("旧配置无法开启上报", !new AppSettings { AnonymousStats = true, CrashReports = true }.AnonymousStats &&
                !new AppSettings { CrashReports = true }.CrashReports);
            var legacySettings = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(
                """{"NotifyWeeklyReport":true,"NotifyProductNews":true,"FollowJunctions":true,"AutoCleanIncludeBrowser":false,"LogLevel":"debug","AnonymousStats":true,"CrashReports":true,"Theme":"dark","Whitelist":["C:\\Keep"],"QuarantineDays":14}""")!;
            Check("移除旧设置仍兼容已有配置并保留隐私与白名单", legacySettings.Theme == "dark" &&
                legacySettings.QuarantineDays == 14 && legacySettings.Whitelist.SequenceEqual(new[] { @"C:\Keep" }) &&
                !legacySettings.AnonymousStats && !legacySettings.CrashReports);
            Check("提权注册表使用精确允许列表", ElevatedService.IsAllowedStartupKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run") &&
                !ElevatedService.IsAllowedStartupKey(@"Software\Microsoft\Windows\CurrentVersion\Policies") &&
                !ElevatedService.IsAllowedStartupKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run\child"));
            Check("提权结果不能覆盖任意文件", !ElevatedService.IsSafeResultPath(keep) &&
                ElevatedService.IsSafeResultPath(Path.Combine(Path.GetTempPath(), $"CleanOptimize_elev_{Guid.NewGuid():N}.json")));
            try { Apps.AppInventoryService.BuildUninstallProcess(@"cmd.exe /c echo injected"); Check("拒绝命令解释器卸载入口", false); }
            catch (InvalidOperationException) { Check("拒绝命令解释器卸载入口", true); }
            var msi = Apps.AppInventoryService.BuildUninstallProcess("MsiExec.exe /X{00000000-0000-0000-0000-000000000000}");
            Check("MSI 卸载直接使用系统程序", msi.FileName == Path.Combine(Environment.SystemDirectory, "msiexec.exe") && !msi.Arguments.Contains("/c "));

            var shredFile = Path.Combine(root, "shred.tmp");
            File.WriteAllText(shredFile, "temporary test data");
            Check("粉碎普通沙盒文件", Shredder.Shred(shredFile) && !File.Exists(shredFile));
            Check("粉碎拒绝系统目录", !Shredder.Shred(Environment.SystemDirectory));
            var hardLink = Path.Combine(root, "hardlink.tmp");
            if (CreateHardLinkW(hardLink, keep, IntPtr.Zero))
                Check("粉碎拒绝硬链接且保持原文件", !Shredder.Shred(hardLink) && File.ReadAllText(keep) == "keep");
            else Check("硬链接测试样本创建", false);

            var nested = Path.Combine(root, "nested");
            Directory.CreateDirectory(nested);
            File.WriteAllText(Path.Combine(nested, "match.log"), "log");
            File.WriteAllText(Path.Combine(nested, "keep.bin"), "bin");
            var rule = new ProbeRule(root);
            var scan = rule.ScanAsync(new ScanContext { SkipRecentMinutes = 0 }).GetAwaiter().GetResult();
            Check("递归扫描遵守文件通配模式", scan.Items.Count == 1 && scan.Items[0].Path.EndsWith("match.log"));
            var excluded = rule.ScanAsync(new ScanContext { ExcludedPaths = new() { root } }).GetAwaiter().GetResult();
            Check("清理规则跳过直接排除根目录", excluded.Items.Count == 0);
            var catalog = new RuleCatalog();
            var privacy = new ScannerService(catalog).ScanAsync(catalog.Rules.Where(r => r.Group == RuleGroup.Privacy),
                new ScanContext(), null).GetAwaiter().GetResult();
            Check("隐私扫描默认不选中", privacy.Count > 0 && privacy.All(r => !r.DefaultSelected));

            var target = Path.Combine(root, "target");
            var link = Path.Combine(root, "link");
            Directory.CreateDirectory(target);
            File.WriteAllText(Path.Combine(target, "keep.log"), "safe");
            if (RuntimeInterop.CreateJunction(link, target))
            {
                Check("清理与显式回收拒绝链接中的目标", SafetyGuard.ValidateForClean(Path.Combine(link, "keep.log"), new[] { root }) != null &&
                    SafetyGuard.ValidateForExplicitFileRecycle(Path.Combine(link, "keep.log")) != null);
                Check("粉碎拒绝目录链接且保持目标", !Shredder.Shred(link) && File.ReadAllText(Path.Combine(target, "keep.log")) == "safe");
                var duplicates = new DuplicateFileService().ScanAsync(new[] { link }, 0, CancellationToken.None, null).GetAwaiter().GetResult();
                Check("重复扫描不遍历链接根目录", duplicates.ScannedFiles == 0);
                var emptyParent = Path.Combine(root, "linkParent");
                Directory.CreateDirectory(emptyParent);
                Check("创建空目录判定链接样本", RuntimeInterop.CreateJunction(Path.Combine(emptyParent, "child"), target));
                var empty = new EmptyFolderService().ScanAsync(new[] { emptyParent }, 8, CancellationToken.None).GetAwaiter().GetResult();
                Check("含目录链接的父目录不判空", empty.Count == 0);
            }
            else Check("创建链接回归测试样本", false);

            using var current = Process.GetCurrentProcess();
            Check("进程检测接受 exe 后缀", ProcessUtil.AnyRunning(current.ProcessName + ".exe"));
            Check("进程检测接受不带后缀名称", ProcessUtil.AnyRunning(current.ProcessName));

            var largeRoot = Path.Combine(root, "largeDuplicates");
            Directory.CreateDirectory(largeRoot);
            var bytes = new byte[128 * 1024];
            Random.Shared.NextBytes(bytes);
            File.WriteAllBytes(Path.Combine(largeRoot, "a.bin"), bytes);
            File.WriteAllBytes(Path.Combine(largeRoot, "b.bin"), bytes);
            bytes[^1] ^= 0xff;
            File.WriteAllBytes(Path.Combine(largeRoot, "differentTail.bin"), bytes);
            var largeDuplicates = new DuplicateFileService().ScanAsync(new[] { largeRoot }, 0, CancellationToken.None, null).GetAwaiter().GetResult();
            Check("相同前缀不同尾部不误判重复", largeDuplicates.Groups.Count == 1 && largeDuplicates.Groups[0].Files.Count == 2 &&
                largeDuplicates.Groups[0].Files.All(f => !f.Path.EndsWith("differentTail.bin")));
            DuplicateFileService.AutoSelect(largeDuplicates.Groups, keepNewest: true);
            Check("重复文件自动选择保留一份", largeDuplicates.Groups[0].Files.Count(f => f.Selected) == 1);

            using var cancel = new CancellationTokenSource();
            var cleanRule = new ProbeRule(root, cancel);
            var item = new CleanItem { Path = keep, Size = 4 };
            var request = new CleanRequestRule { Rule = cleanRule, Result = new RuleResult(), Selected = new List<CleanItem> { item } };
            var summary = new CleanService().CleanAsync(new[] { request, request }, cancel.Token, null).GetAwaiter().GetResult();
            Check("规则之间取消仍保留清理汇总", summary.Cancelled && summary.SuccessCount == 1 && summary.FreedBytes == 4);
            Check("取消结果记录到历史", HistoryService.LoadAll().Any(h => h.Type == "CleanCancelled"));
            var cleanRoot = Path.Combine(root, "cleanPipeline");
            Directory.CreateDirectory(cleanRoot);
            var remove = Path.Combine(cleanRoot, "remove.tmp");
            var protect = Path.Combine(cleanRoot, "protect.tmp");
            var busy = Path.Combine(cleanRoot, "busy.tmp");
            foreach (var path in new[] { remove, protect, busy }) File.WriteAllText(path, "12345");
            var selected = new[] { remove, protect, busy, Path.Combine(cleanRoot, "already-gone.tmp") }
                .Select(p => new CleanItem { Path = p, Size = 5 }).ToList();
            SettingsService.Current.Whitelist.Add(protect);
            SettingsService.Current.QuarantineEnabled = false;
            try
            {
                using var busyStream = new FileStream(busy, FileMode.Open, FileAccess.Read, FileShare.None);
                var realSummary = new CleanService().CleanAsync(new[] { new CleanRequestRule
                {
                    Rule = new SandboxCleanRule(cleanRoot), Result = new RuleResult(), Selected = selected,
                } }, CancellationToken.None, null).GetAwaiter().GetResult();
                Check("实际清理链路保留白名单及占用文件", !File.Exists(remove) && File.Exists(protect) && File.Exists(busy) &&
                    realSummary.SuccessCount == 1 && realSummary.SkipCount == 3 && realSummary.FreedBytes == 5);
            }
            finally { SettingsService.Current.Whitelist.Remove(protect); SettingsService.Current.QuarantineEnabled = true; }

            var quarantine = QuarantineService.Instance;
            var manifest = Path.Combine(SettingsService.DataDir, "quarantine.json");
            using (var manifestLock = new FileStream(manifest, FileMode.Open, FileAccess.Read, FileShare.None))
                Check("隔离清单写入失败时不移动源文件", !quarantine.MoveIntoQuarantine(protect, "test", out _) && File.Exists(protect));

            var diskRoot = Path.Combine(root, "diskAnalysis");
            for (int i = 0; i < 8; i++)
            {
                var child = Path.Combine(diskRoot, i.ToString());
                Directory.CreateDirectory(child);
                File.WriteAllBytes(Path.Combine(child, "sample.bin"), new byte[1024]);
            }
            var analyzer = new SpaceAnalysisService();
            var disk = analyzer.AnalyzeAsync(diskRoot, 512, CancellationToken.None, null).GetAwaiter().GetResult();
            Check("磁盘分析多线程统计与目录汇总一致", disk.Completed && disk.ScannedFiles == 8 && disk.ScannedBytes == 8192 &&
                disk.Root.Size == 8192 && disk.LargeFiles.Count == 8 && disk.Categories.Sum(c => c.Bytes) == 8192);
            using var diskCancel = new CancellationTokenSource();
            try
            {
                analyzer.AnalyzeAsync(diskRoot, 512, diskCancel.Token,
                    new CallbackProgress<SpaceAnalysisService.ProgressInfo>(_ => diskCancel.Cancel())).GetAwaiter().GetResult();
                Check("磁盘分析运行中取消不闪退", false);
            }
            catch (OperationCanceledException) { Check("磁盘分析运行中取消不闪退", true); }
            var topRoot = Path.Combine(root, "topFiles");
            Directory.CreateDirectory(topRoot);
            for (int i = 1; i <= 1200; i++) File.WriteAllBytes(Path.Combine(topRoot, i + ".bin"), new byte[i]);
            var top = analyzer.AnalyzeAsync(topRoot, 1, CancellationToken.None, null).GetAwaiter().GetResult();
            Check("大文件仅保留最大1000项且总量正确", top.LargeFiles.Count == 1000 && top.LargeFiles.Min(f => f.Size) == 201 && top.ScannedFiles == 1200 && top.ScannedBytes == 720600);

            HistoryService.Clear();
            Parallel.For(0, 40, i => HistoryService.Add("ConcurrentTest", i.ToString()));
            Check("并发历史写入不丢记录", HistoryService.LoadAll().Count(h => h.Type == "ConcurrentTest") == 40);
        }
        catch (Exception ex) { report("回归测试异常", false, ex.ToString()); }
        finally { FileUtil.DeleteDirectoryTree(root); }
    }

    private static void CheckDuplicateRecycle(Action<string, bool, string> report)
    {
        void Check(string name, bool ok, string detail = "") => report(name, ok, detail);
        // Exercise the actual protected personal-file path, not only AppData/Temp.
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var root = Path.Combine(profile, "CleanOptimizeDuplicateTest_" + Guid.NewGuid().ToString("N"));
        if (!PathUtil.IsLocalPath(root) || PathUtil.HasReparseAncestor(root)) throw new InvalidOperationException("回收测试目录无效");
        Directory.CreateDirectory(root);
        var blocked = Path.Combine(root, "blocked.bin");
        try
        {
            var keep = Path.Combine(root, "keep.bin");
            var recycle = Path.Combine(root, "recycle.bin");
            var changed = Path.Combine(root, "changed.bin");
            foreach (var path in new[] { keep, recycle, blocked, changed }) File.WriteAllBytes(path, new byte[4096]);
            using var recycledFixture = new RecycleTestScope();
            recycledFixture.Track(recycle);
            var groups = new DuplicateFileService().ScanAsync(new[] { root }, 1, CancellationToken.None, null).GetAwaiter().GetResult().Groups;
            Check("个人目录重复文件复现旧保护冲突", groups.Count == 1 &&
                SafetyGuard.ValidateForClean(recycle, new[] { root }) != null && SafetyGuard.ValidateForExplicitFileRecycle(recycle) == null);
            Check("显式文件回收仍拒绝目录与系统文件", SafetyGuard.ValidateForExplicitFileRecycle(root) != null &&
                SafetyGuard.ValidateForExplicitFileRecycle(Path.Combine(Environment.SystemDirectory, "kernel32.dll")) != null);
            Check("显式文件回收仍拒绝网络与白名单", SafetyGuard.ValidateForExplicitFileRecycle(@"\\example.invalid\share\a.bin") != null &&
                SafetyGuard.ValidateForExplicitFileRecycle(blocked, new HashSet<string> { root }) != null);
            foreach (var file in groups[0].Files) file.Selected = file.Path != keep;
            SettingsService.Current.Whitelist.Add(blocked);
            File.WriteAllText(changed, "modified after scanning");
            var before = ShellUtil.QueryRecycleBin(Path.GetPathRoot(root)).Count;
            var result = DuplicateRecycleService.RecycleSelectedAsync(groups).GetAwaiter().GetResult();
            var after = ShellUtil.QueryRecycleBin(Path.GetPathRoot(root)).Count;
            Check("个人目录选中副本实际移入回收站", result.RecycledPaths.SequenceEqual(new[] { recycle }) && !File.Exists(recycle) && after > before,
                string.Join("; ", result.Failures.Select(f => f.Reason)));
            Check("回收保留未选副本与白名单文件", File.Exists(keep) && File.Exists(blocked) && File.ReadAllBytes(keep).Length == 4096);
            Check("回收拒绝已变化文件且仅统计成功字节", File.Exists(changed) && result.RecycledBytes == 4096 && result.Failures.Count == 2);

            SettingsService.Current.Whitelist.Remove(blocked);
            var all = new DuplicateGroup { Hash = FileUtil.Sha256OfFile(keep), Size = 4096 };
            all.Files.Add(new DuplicateFile { Path = keep, Size = 4096, Selected = true });
            all.Files.Add(new DuplicateFile { Path = blocked, Size = 4096, Selected = true });
            var allResult = DuplicateRecycleService.RecycleSelectedAsync(new[] { all }).GetAwaiter().GetResult();
            Check("全部选中时不回收任何副本", allResult.RecycledPaths.Count == 0 && allResult.Failures.Count == 2 && File.Exists(keep) && File.Exists(blocked));
            all.Files[0].Selected = false;
            all.Files[0].Path = Path.Combine(root, "missing.bin");
            var missingResult = DuplicateRecycleService.RecycleSelectedAsync(new[] { all }).GetAwaiter().GetResult();
            Check("保留副本失效时不回收最后一份", missingResult.RecycledPaths.Count == 0 && missingResult.Failures.Count == 1 && File.Exists(blocked));
            all.Files[0].Path = keep;
            using (var locked = new FileStream(blocked, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                var lockedResult = DuplicateRecycleService.RecycleSelectedAsync(new[] { all }).GetAwaiter().GetResult();
                Check("占用文件返回原因且不虚报成功", lockedResult.RecycledPaths.Count == 0 && lockedResult.RecycledBytes == 0 &&
                    lockedResult.Failures.Count == 1 && !string.IsNullOrEmpty(lockedResult.Failures[0].Reason));
            }
        }
        finally
        {
            SettingsService.Current.Whitelist.Remove(blocked);
            // Exact UUID sandbox created above; never clean the user's directories.
            FileUtil.DeleteDirectoryTree(root);
        }
    }

    private static void CheckManualTools(Action<string, bool, string> report)
    {
        void Check(string name, bool ok, string detail = "") => report(name, ok, detail);
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var root = Path.Combine(profile, "CleanOptimizeToolTest_" + Guid.NewGuid().ToString("N"));
        if (!PathUtil.IsLocalPath(root) || PathUtil.HasReparseAncestor(root)) throw new InvalidOperationException("测试目录无效");
        Directory.CreateDirectory(root);
        var protectedFile = Path.Combine(root, "protected.txt");
        try
        {
            var file = Path.Combine(root, "shred.txt");
            File.WriteAllText(file, "owned test content");
            Check("自动粉碎仍拒绝个人目录", !Shredder.Shred(file) && File.Exists(file));
            var shredded = Shredder.ShredSelected(file, out var error);
            Check("显式粉碎个人目录测试文件", shredded && error == null && !File.Exists(file), error ?? "");
            Check("显式粉碎仍拒绝整个用户目录和系统目录",
                SafetyGuard.ValidateForExplicitShred(profile, profile) != null &&
                SafetyGuard.ValidateForExplicitShred(Environment.SystemDirectory, Environment.SystemDirectory) != null);
            File.WriteAllText(protectedFile, "keep");
            SettingsService.Current.Whitelist.Add(protectedFile);
            Check("显式粉碎保留白名单", !Shredder.ShredSelected(protectedFile, out _) && File.ReadAllText(protectedFile) == "keep");
            SettingsService.Current.Whitelist.Remove(protectedFile);
            var hardlink = Path.Combine(root, "hardlink.txt");
            Check("显式粉碎拒绝硬链接且原内容不变", CreateHardLinkW(hardlink, protectedFile, IntPtr.Zero) &&
                !Shredder.ShredSelected(hardlink, out _) && File.ReadAllText(protectedFile) == "keep");
            var link = Path.Combine(root, "link");
            var target = Path.Combine(root, "target");
            Directory.CreateDirectory(target);
            File.WriteAllText(Path.Combine(target, "keep.txt"), "keep");
            Check("显式粉碎拒绝目录链接", RuntimeInterop.CreateJunction(link, target) &&
                !Shredder.ShredSelected(link, out _) && File.ReadAllText(Path.Combine(target, "keep.txt")) == "keep");
            var folder = Path.Combine(root, "selected-folder");
            Directory.CreateDirectory(Path.Combine(folder, "empty"));
            File.WriteAllText(Path.Combine(folder, "sample.txt"), "owned sample");
            Check("显式粉碎所选个人子目录", Shredder.ShredSelected(folder, out _) && !Directory.Exists(folder));
            var lockedPath = Path.Combine(root, "locked.txt");
            File.WriteAllText(lockedPath, "keep");
            using (var handle = new FileStream(lockedPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                Check("粉碎占用文件返回具体原因", !Shredder.ShredSelected(lockedPath, out var lockedError) && !string.IsNullOrWhiteSpace(lockedError));

            var empty = Path.Combine(root, "empty-tree");
            Directory.CreateDirectory(Path.Combine(empty, "child"));
            using var recycledFixture = new RecycleTestScope();
            recycledFixture.Track(empty);
            var changed = Path.Combine(root, "was-empty");
            Directory.CreateDirectory(changed);
            File.WriteAllText(Path.Combine(changed, "new.txt"), "new data after scan");
            var result = EmptyFolderService.RecycleSelectedAsync(new[] { empty, changed, link }).GetAwaiter().GetResult();
            Check("个人空目录回收成功且保留后来新增文件和链接目标", result.RecycledPaths.SequenceEqual(new[] { empty }) &&
                !Directory.Exists(empty) && result.Failures.Count == 2 && File.Exists(Path.Combine(changed, "new.txt")) && Directory.Exists(target));
            Check("空目录回收拒绝整个文档目录与白名单", SafetyGuard.ValidateForEmptyFolder(profile) != null &&
                SafetyGuard.ValidateForEmptyFolder(changed, new HashSet<string> { changed }) != null);
        }
        finally { SettingsService.Current.Whitelist.Remove(protectedFile); FileUtil.DeleteDirectoryTree(root); }
    }

    private sealed class CallbackProgress<T>(Action<T> callback) : IProgress<T>
    {
        public void Report(T value) => callback(value);
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool CreateHardLinkW(string name, string existing, IntPtr attributes);

    private sealed class ProbeRule(string root, CancellationTokenSource? cancel = null) : RuleBase
    {
        public override string Id => "regression_probe";
        public override string Name => "Regression probe";
        public override RuleGroup Group => RuleGroup.SystemJunk;
        public override RiskLevel Risk => RiskLevel.Safe;
        public override IEnumerable<string> AllowedRoots() => new[] { root };
        public override Task<RuleResult> ScanAsync(ScanContext ctx)
        {
            var result = new RuleResult();
            AddDirFiles(result, ctx, root, "*.log", recursive: true);
            return Task.FromResult(result);
        }
        public override Task CleanAsync(CleanContext ctx)
        {
            ctx.Report(ctx.Selected[0], CleanItemStatus.Success, "simulated");
            cancel?.Cancel();
            return Task.CompletedTask;
        }
    }

    private sealed class SandboxCleanRule(string root) : RuleBase
    {
        public override string Id => "sandbox_clean";
        public override string Name => "Sandbox clean";
        public override RuleGroup Group => RuleGroup.SystemJunk;
        public override RiskLevel Risk => RiskLevel.Safe;
        public override IEnumerable<string> AllowedRoots() => new[] { root };
        public override Task<RuleResult> ScanAsync(ScanContext ctx) => Task.FromResult(new RuleResult());
    }
}
