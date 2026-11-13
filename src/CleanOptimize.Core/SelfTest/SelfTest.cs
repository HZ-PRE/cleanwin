using System.Diagnostics;
using System.IO;
using CleanOptimize.Core.Analysis;
using CleanOptimize.Core.Models;
using CleanOptimize.Core.Rules;
using CleanOptimize.Core.Security;
using CleanOptimize.Core.Store;
using CleanOptimize.Core.Utils;

namespace CleanOptimize.Core.SelfTest;

/// <summary>
/// 安全自测：--selftest 启动时运行，覆盖 PRD 第 53 章的必测安全场景（可离线自动化）。
/// 任何一项失败都应阻止发布。
/// </summary>
public static class SelfTest
{
    public static int RunAll()
    {
        var dataDir = CreateIsolatedEnvironment();
        try { return RunChecks(); }
        finally { FileUtil.DeleteDirectoryTree(dataDir); }
    }

    public static string CreateIsolatedEnvironment()
    {
        var dataDir = Path.Combine(Path.GetTempPath(), "CleanOptimizeTestData_" + Guid.NewGuid().ToString("N"));
        SettingsService.InitializeSelfTest(dataDir);
        return dataDir;
    }

    private static int RunChecks()
    {
        int failed = 0;
        void Check(string name, bool ok, string detail = "")
        {
            Console.WriteLine($"{(ok ? "[PASS]" : "[FAIL]")} {name}{(detail.Length > 0 ? "  — " + detail : "")}");
            if (!ok) failed++;
        }

        Console.WriteLine("== 清理优化大师 安全自测 ==");

        // 1. SafetyGuard 拒绝表
        var user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Check("拒绝删除桌面", SafetyGuard.FindDenyReason(Path.Combine(user, "Desktop", "a.txt")) != null);
        Check("拒绝删除文档", SafetyGuard.FindDenyReason(Path.Combine(user, "Documents", "report.docx")) != null);
        Check("拒绝删除图片", SafetyGuard.FindDenyReason(Path.Combine(user, "Pictures", "a.jpg")) != null);
        Check("拒绝删除下载", SafetyGuard.FindDenyReason(Path.Combine(user, "Downloads", "setup.exe")) != null);
        Check("拒绝删除用户主目录", SafetyGuard.FindDenyReason(user) != null);
        Check("拒绝删除 System32", SafetyGuard.FindDenyReason(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "a.dll")) != null);
        Check("拒绝删除 WinSxS", SafetyGuard.FindDenyReason(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "WinSxS", "x.dll")) != null);
        Check("拒绝删除 Windows\\Installer", SafetyGuard.FindDenyReason(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Installer", "x.msi")) != null);
        Check("拒绝删除 Program Files", SafetyGuard.FindDenyReason(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "App", "a.dll")) != null);
        Check("拒绝删除磁盘根", SafetyGuard.FindDenyReason("C:\\") != null);

        // 2. SafetyGuard 允许临时目录
        var tempRoot = Path.GetTempPath();
        var tempFile = Path.Combine(tempRoot, "guardtest.tmp");
        Check("允许清理用户临时目录文件", SafetyGuard.FindDenyReason(tempFile) == null);
        var allowed = SafetyGuard.ValidateForClean(tempFile, new[] { PathUtil.Normalize(tempRoot) });
        Check("允许根校验通过(临时目录)", allowed == null, allowed ?? "");

        // 3. 规则允许根约束：用桌面路径冒充 temp 规则目标必须被拒
        var denied = SafetyGuard.ValidateForClean(Path.Combine(user, "Desktop", "x.tmp"), new[] { PathUtil.Normalize(tempRoot) });
        Check("越界目标被拒绝(不在允许根)", denied != null, denied ?? "");

        // 4. 清理 + 隔离区往返（在隔离沙盒目录中进行）
        var sandbox = Path.Combine(tempRoot, "CleanOptimizeSelfTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sandbox);
        try
        {
            var probe = Path.Combine(sandbox, "junk.log");
            File.WriteAllText(probe, "selftest");
            var quarantine = QuarantineService.Instance;
            var moved = quarantine.MoveIntoQuarantine(probe, "selftest", out var err);
            Check("文件移入隔离区", moved, err ?? "");
            var entry = quarantine.Entries.FirstOrDefault(e => e.OriginalPath == probe);
            Check("隔离清单有记录", entry != null);
            if (entry != null)
            {
                var restoreErr = quarantine.Restore(entry.Id);
                Check("隔离恢复成功", restoreErr == null, restoreErr ?? "");
                Check("恢复后文件存在且内容一致", File.Exists(probe) && File.ReadAllText(probe) == "selftest");

                // 永久删除：再造一个文件进隔离区后删除
                var probe2 = Path.Combine(sandbox, "junk2.log");
                File.WriteAllText(probe2, "selftest2");
                var moved2 = quarantine.MoveIntoQuarantine(probe2, "selftest", out var err2);
                Check("第二个文件移入隔离区", moved2, err2 ?? "");
                var entry2 = quarantine.Entries.FirstOrDefault(e => e.OriginalPath == probe2);
                if (entry2 != null)
                {
                    var stored = Path.Combine(SettingsService.QuarantineDir, entry2.StoredName);
                    using (var locked = new FileStream(stored, FileMode.Open, FileAccess.Read, FileShare.None))
                    {
                        Check("隔离区删除失败保留记录", !quarantine.DeletePermanent(entry2.Id, out var freed) && freed == 0 &&
                            quarantine.Entries.Any(e => e.Id == entry2.Id));
                        Check("清空隔离区保留占用项目", quarantine.PurgeAll() == 0 && quarantine.Entries.Any(e => e.Id == entry2.Id));
                    }
                    Check("永久删除隔离项", quarantine.DeletePermanent(entry2.Id, out _) && !File.Exists(stored));
                }
                var readOnly = Path.Combine(sandbox, "readonly.txt");
                File.WriteAllText(readOnly, "read-only test");
                File.SetAttributes(readOnly, FileAttributes.ReadOnly);
                var movedReadOnly = quarantine.MoveIntoQuarantine(readOnly, "selftest", out _);
                var readOnlyEntry = quarantine.Entries.FirstOrDefault(e => e.OriginalPath == readOnly);
                Check("隔离区永久删除只读文件", movedReadOnly && readOnlyEntry != null &&
                    quarantine.DeletePermanent(readOnlyEntry.Id, out _, out _) &&
                    !File.Exists(Path.Combine(SettingsService.QuarantineDir, readOnlyEntry.StoredName)));
            }

            // 5. 仅使用测试数据验证链接边界，绝不以真实文档目录作为删除测试目标。
            var docs = Path.Combine(sandbox, "junctionTarget");
            Directory.CreateDirectory(docs);
            File.WriteAllText(Path.Combine(docs, "keep.txt"), "keep");
            var inner = Path.Combine(sandbox, "inner");
            Directory.CreateDirectory(inner);
            var link = Path.Combine(inner, "linkToDocs");
            File.WriteAllText(Path.Combine(inner, "a.txt"), "1");
            try
            {
                if (RuntimeInterop.CreateJunction(link, docs))
                {
                    var (ok, fail, _) = FileUtil.DeleteDirectoryTree(inner);
                    var docsSurvives = File.ReadAllText(Path.Combine(docs, "keep.txt")) == "keep";
                    Check("删除含 Junction 的目录不破坏目标", docsSurvives,
                        docsSurvives ? "" : "文档目录被破坏，禁止发布！");
                }
                else
                {
                    Check("Junction 测试环境创建成功", false, "无法验证链接边界");
                }
            }
            catch (Exception ex)
            {
                Check("Junction 测试异常", false, ex.Message);
            }
        }
        finally
        {
            try { Directory.Delete(sandbox, true); } catch { }
        }

        // 6. 重复文件算法正确性
        try
        {
            var dupRoot = Path.Combine(tempRoot, "CleanOptimizeDup_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(dupRoot, "a"));
            Directory.CreateDirectory(Path.Combine(dupRoot, "b"));
            var data = new byte[12345];
            Random.Shared.NextBytes(data);
            File.WriteAllBytes(Path.Combine(dupRoot, "a", "x.bin"), data);
            File.WriteAllBytes(Path.Combine(dupRoot, "b", "y.bin"), data);
            File.WriteAllBytes(Path.Combine(dupRoot, "b", "z.bin"), new byte[12345]);

            var svc = new DuplicateFileService();
            var result = svc.ScanAsync(new[] { dupRoot }, 1024, CancellationToken.None, null).GetAwaiter().GetResult();
            // x 与 y 相同内容 → 一组；z 全零也是 12345 字节，与上面相同大小不同内容 → 不得归入
            var group = result.Groups.FirstOrDefault(g => g.Files.Count == 2);
            Check("重复文件检测(相同内容归一组)", group != null);
            Check("不同内容不误判重复", result.Groups.All(g => g.Hash == null || g.Files.Count <= 2) &&
                                          !result.Groups.Any(g => g.Files.Count > 2));
            var overlap = svc.ScanAsync(new[] { dupRoot, Path.Combine(dupRoot, "a"), dupRoot }, 1024, CancellationToken.None, null).GetAwaiter().GetResult();
            Check("重叠扫描目录不重复统计", overlap.ScannedFiles == 3 && overlap.Groups.Count == 1 && overlap.Groups[0].Files.Count == 2);
            SettingsService.Current.ExcludedPaths.Add(Path.Combine(dupRoot, "a"));
            var excluded = svc.ScanAsync(new[] { Path.Combine(dupRoot, "a") }, 0, CancellationToken.None, null).GetAwaiter().GetResult();
            Check("直接选择排除目录仍跳过", excluded.ScannedFiles == 0);
            SettingsService.Current.ExcludedPaths.Clear();
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            try { FileUtil.Sha256OfFile(Path.Combine(dupRoot, "a", "x.bin"), ct: cancelled.Token); Check("哈希响应取消", false); }
            catch (OperationCanceledException) { Check("哈希响应取消", true); }
            try { Directory.Delete(dupRoot, true); } catch { }
        }
        catch (Exception ex)
        {
            Check("重复文件检测异常", false, ex.Message);
        }

        // 7. 启动项枚举不抛异常且有条目
        try
        {
            var st = new Startup.StartupService();
            var items = st.Enumerate();
            Check("启动项枚举", items.All(i => i != null), $"共 {items.Count} 项（空列表允许）");
        }
        catch (Exception ex)
        {
            Check("启动项枚举异常", false, ex.Message);
        }

        // 8. 应用清单枚举
        try
        {
            var inv = new Apps.AppInventoryService();
            var apps = inv.EnumerateWin32();
            Check("已安装应用枚举", apps.All(i => i != null), $"共 {apps.Count} 个（空列表允许）");
        }
        catch (Exception ex)
        {
            Check("应用清单枚举异常", false, ex.Message);
        }

        // 9. 真实规则扫描冒烟（限时 90 秒，验证不抛异常）
        try
        {
            var catalog = new RuleCatalog();
            var scanner = new ScannerService(catalog);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            var ctx = scanner.BuildContext(cts.Token);
            var results = scanner.ScanAsync(catalog.Rules.Where(r => r.Group != RuleGroup.Privacy).ToList(),
                ctx, null).GetAwaiter().GetResult();
            var withError = results.Where(r => r.Error != null).ToList();
            Check("需确认项目不默认勾选", results.Where(r => r.Risk >= RiskLevel.Confirm).All(r => !r.DefaultSelected));
            Check("规则扫描冒烟(真实机器)", withError.Count == 0,
                $"{results.Count} 条规则, 发现 {SizeText.OfBytes(results.Sum(r => r.TotalSize))}" +
                (withError.Count > 0 ? ", 错误: " + string.Join(";", withError.Select(e => e.RuleId + ":" + e.Error)) : ""));
        }
        catch (OperationCanceledException)
        {
            Check("规则扫描冒烟超时", false, "90s 超时，未完成验证");
        }
        catch (Exception ex)
        {
            Check("规则扫描冒烟异常", false, ex.Message);
        }

        // 10. 空文件夹检测：只报空树、父目录覆盖子目录、含文件不误判
        try
        {
            var efRoot = Path.Combine(tempRoot, "CleanOptimizeEmpty_" + Guid.NewGuid().ToString("N"));
            var emptyA = Path.Combine(efRoot, "emptyA", "deep", "deeper");
            var hasFile = Path.Combine(efRoot, "hasfile");
            Directory.CreateDirectory(emptyA);
            Directory.CreateDirectory(hasFile);
            Directory.CreateDirectory(Path.Combine(efRoot, "hasfile", "sub"));
            using var recycledFixture = new RecycleTestScope();
            recycledFixture.Track(Path.Combine(efRoot, "emptyA"));
            File.WriteAllText(Path.Combine(hasFile, "keep.txt"), "1");

            var efSvc = new EmptyFolderService();
            var found = efSvc.ScanAsync(new[] { efRoot }, 8, CancellationToken.None, null).GetAwaiter().GetResult();
            var foundPaths = found.Select(f => PathUtil.Normalize(f.Path)).ToList();
            Check("空文件夹: 检出空树顶层", foundPaths.Contains(PathUtil.Normalize(Path.Combine(efRoot, "emptyA"))) && found.Count == 2,
                string.Join(", ", foundPaths));
            Check("空文件夹: 含文件目录不误判", !foundPaths.Contains(PathUtil.Normalize(hasFile)));
            Check("空文件夹: 含文件目录内仍能检出空子目录", foundPaths.Contains(PathUtil.Normalize(Path.Combine(hasFile, "sub"))));
            var overlapping = efSvc.ScanAsync(new[] { efRoot, Path.Combine(efRoot, "emptyA"), efRoot }, 8, CancellationToken.None, null).GetAwaiter().GetResult();
            Check("空文件夹: 重叠根目录不丢失结果", overlapping.Count == 2);
            var limited = efSvc.ScanAsync(new[] { efRoot }, 1, CancellationToken.None, null).GetAwaiter().GetResult();
            Check("空文件夹: 达到深度限制不误判", limited.Count == 0);
            var skippedRoot = Path.Combine(efRoot, "skipped");
            Directory.CreateDirectory(Path.Combine(skippedRoot, "Recovery"));
            File.WriteAllText(Path.Combine(skippedRoot, "Recovery", "keep.txt"), "keep");
            var skipped = efSvc.ScanAsync(new[] { skippedRoot }, 8, CancellationToken.None, null).GetAwaiter().GetResult();
            Check("空文件夹: 未扫描的受保护子目录不当成空目录", skipped.Count == 0);

            // 删除后复扫应为空结果
            var del = ShellUtil.RecycleToBin(Path.Combine(efRoot, "emptyA"));
            Check("空文件夹: 回收站删除成功", del);
            try { Directory.Delete(efRoot, true); } catch { }
        }
        catch (Exception ex)
        {
            Check("空文件夹检测异常", false, ex.Message);
        }

        // 11. 系统监控: 内存数值合理,CPU 第二次采样有值
        try
        {
            var mon = new SystemMonitorService();
            var s1 = mon.Sample();
            Thread.Sleep(120);
            var s2 = mon.Sample();
            Check("系统监控: 内存总量>0", s2.RamTotalBytes > 0 && s2.RamPercent is > 0 and <= 100,
                $"{SizeText.OfBytes(s2.RamTotalBytes, 0)} / {s2.RamPercent:F0}%");
            Check("系统监控: CPU 差分采样", s2.CpuPercent is >= 0 and <= 100, s2.CpuPercent?.ToString("F0") + "%");
        }
        catch (Exception ex)
        {
            Check("系统监控异常", false, ex.Message);
        }

        // 12. 健康评分分级
        try
        {
            var perfect = HealthScoreService.Compute(0, 0, 60);
            Check("健康评分: 全优 >= 85", perfect.Score >= 85, perfect.Score + " " + perfect.LevelKey);
            var awful = HealthScoreService.Compute(20L * 1024 * 1024 * 1024, 5, 4);
            Check("健康评分: 恶劣 < 50", awful.Score < 50, awful.Score + " " + awful.LevelKey);
            Check("健康评分: 分级色对应", awful.BrushKey == "DangerBrush" && perfect.BrushKey is "SuccessBrush" or "TealBrush");
        }
        catch (Exception ex)
        {
            Check("健康评分异常", false, ex.Message);
        }

        // 13. 系统维护检查: 只读且结构完整
        try
        {
            var maint = SystemMaintenanceService.CheckAll();
            Check("系统维护: 返回 3 项", maint.Count == 3 && maint.All(m => m.State is "on" or "off" or "unknown"),
                string.Join(", ", maint.Select(m => $"{m.Id}={m.State}")));
        }
        catch (Exception ex)
        {
            Check("系统维护检查异常", false, ex.Message);
        }

        // 14. 自定义清理目录规则:端到端(添加→扫描→命中),并验证用户数据目录被拦截
        try
        {
            var cfRoot = Path.Combine(tempRoot, "CleanOptimizeCustom_" + Guid.NewGuid().ToString("N"));
            var sub = Path.Combine(cfRoot, "cache");
            Directory.CreateDirectory(sub);
            foreach (var n in new[] { "a.tmp", "b.tmp", "c.log" })
            {
                var f = Path.Combine(sub, n);
                File.WriteAllText(f, "x");
                // 绕过"跳过最近文件"约束
                File.SetLastWriteTime(f, DateTime.Now.AddHours(-2));
            }

            var savedDirs = Store.SettingsService.Current.CustomCleanDirs.ToList();
            try
            {
                Store.SettingsService.Current.CustomCleanDirs = new List<string> { cfRoot };
                var cfRule = new CustomFoldersRule();
                var scanner2 = new ScannerService();
                var ctx2 = scanner2.BuildContext(CancellationToken.None);
                var res2 = cfRule.ScanAsync(ctx2).GetAwaiter().GetResult();
                Check("自定义目录: 扫描命中全部文件", res2.FileCount == 3 && res2.TotalSize == 3,
                    $"{res2.FileCount} 个 / {res2.TotalSize} 字节");

                var roots = cfRule.AllowedRoots().ToList();
                var denyDesktop = SafetyGuard.ValidateForClean(
                    Path.Combine(user, "Desktop", "x.tmp"), roots, new HashSet<string>());
                Check("自定义目录: 越界桌面路径被拒绝", denyDesktop != null, denyDesktop ?? "");
            }
            finally
            {
                Store.SettingsService.Current.CustomCleanDirs = savedDirs;
                try { Directory.Delete(cfRoot, true); } catch { }
            }
        }
        catch (Exception ex)
        {
            Check("自定义目录规则异常", false, ex.Message);
        }

        // 15. 开机耗时服务:不抛异常,返回 null 或正数(日志未启用时为 null)
        try
        {
            var boot = Analysis.BootTimeService.LastBootSeconds();
            Check("开机耗时服务", boot == null || boot > 0, boot?.ToString("F1") + "s" ?? "null(日志未启用)");
        }
        catch (Exception ex)
        {
            Check("开机耗时服务异常", false, ex.Message);
        }

        RegressionChecks.Run(Check);
        Console.WriteLine(failed == 0 ? "== 全部通过 ==" : $"== {failed} 项失败，禁止发布 ==");
        return failed == 0 ? 0 : 1;
    }
}

internal static class RuntimeInterop
{
    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateSymbolicLinkW(string lpSymlinkFileName, string lpTargetFileName, int dwFlags);

    public static bool CreateJunction(string linkPath, string targetPath)
    {
        // junction 用 cmd mklink /J 创建（符号链接需要特权）
        try
        {
            var psi = new ProcessStartInfo(System.IO.Path.Combine(Environment.SystemDirectory, "cmd.exe"), $"/c mklink /J \"{linkPath}\" \"{targetPath}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
            };
            using var p = Process.Start(psi)!;
            p.WaitForExit(10000);
            return Directory.Exists(linkPath);
        }
        catch { return false; }
    }
}
