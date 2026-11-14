using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using CleanOptimize.Core.Models;
using CleanOptimize.Core.Security;
using CleanOptimize.Core.Store;
using CleanOptimize.Core.Utils;

namespace CleanOptimize.Core.Rules;

public sealed class ScanContext
{
    public CancellationToken Ct { get; init; }
    /// <summary>扫描明细回调（当前扫描路径）。</summary>
    public Action<string>? Detail { get; init; }
    public int SkipRecentMinutes { get; init; } = 10;
    public HashSet<string> Whitelist { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> ExcludedPaths { get; init; } = new();
}

public sealed class CleanContext
{
    public required RuleResult Result { get; init; }
    public required IReadOnlyList<CleanItem> Selected { get; init; }
    public required bool UseQuarantine { get; init; }
    public required CancellationToken Ct { get; init; }
    public required IProgress<CleanProgress>? Progress { get; init; }
    public required Action<CleanItem, CleanItemStatus, string> Report { get; init; }
    public required ISet<string> Whitelist { get; init; }
    public List<string> AllowedRoots { get; } = new();
}

public interface IScanRule
{
    string Id { get; }
    string Name { get; }
    RuleGroup Group { get; }
    RiskLevel Risk { get; }
    bool DefaultSelected { get; }
    bool CanRestore { get; }
    /// <summary>为什么可以清理（展示给用户）。</summary>
    string Reason { get; }
    /// <summary>清理后的影响（展示给用户）。</summary>
    string Impact { get; }
    /// <summary>允许清理的根目录（SafetyGuard 白名单约束）。</summary>
    IEnumerable<string> AllowedRoots();
    Task<RuleResult> ScanAsync(ScanContext ctx);
    Task CleanAsync(CleanContext ctx);
}

public abstract class RuleBase : IScanRule
{
    public abstract string Id { get; }
    public abstract string Name { get; }
    public abstract RuleGroup Group { get; }
    public abstract RiskLevel Risk { get; }
    public virtual bool DefaultSelected => Risk <= RiskLevel.Low;
    public virtual bool CanRestore => Risk <= RiskLevel.Confirm;
    public virtual string Reason => "";
    public virtual string Impact => "";
    public virtual IEnumerable<string> AllowedRoots() { yield break; }

    public abstract Task<RuleResult> ScanAsync(ScanContext ctx);

    public virtual async Task CleanAsync(CleanContext ctx)
    {
        await Task.Run(() =>
        {
            var batch = new List<CleanItem>(64);
            void FlushQuarantine()
            {
                if (batch.Count == 0) return;
                QuarantineService.Instance.MoveBatchIntoQuarantine(batch, Id, ctx.Ct,
                    item => SafetyGuard.ValidateForClean(item.Path, ctx.AllowedRoots, ctx.Whitelist),
                    (item, moved, error) => ctx.Report(item, moved ? CleanItemStatus.Quarantined : CleanItemStatus.Failed, error ?? ""));
                batch.Clear();
            }
            foreach (var item in ctx.Selected)
            {
                ctx.Ct.ThrowIfCancellationRequested();
                var deny = SafetyGuard.ValidateForClean(item.Path, ctx.AllowedRoots, ctx.Whitelist);
                if (deny != null)
                {
                    ctx.Report(item, CleanItemStatus.SkippedWhitelist, deny);
                }
                else if (ctx.UseQuarantine)
                {
                    batch.Add(item);
                    if (batch.Count == 64) FlushQuarantine();
                }
                else if (item.IsDirectory)
                {
                    string? firstFailure = null;
                    var (ok, fail, bytes) = FileUtil.DeleteDirectoryTree(item.Path, (path, reason) => firstFailure ??= path + "\n" + reason);
                    item.Size = bytes;
                    item.DeletedBytes = bytes;
                    ctx.Report(item, fail == 0 ? CleanItemStatus.Success : CleanItemStatus.Failed,
                        fail > 0 ? string.Format(Store.CoreLocalizer.T("clean.skip.subitems", "{0} 个子项被跳过"), fail) + "\n" + firstFailure : "");
                }
                else
                {
                    // Refresh the logical length: a cache file may have changed since scanning.
                    try { item.Size = new FileInfo(item.Path).Length; }
                    catch (Exception ex)
                    {
                        ctx.Report(item, ex is FileNotFoundException or DirectoryNotFoundException ? CleanItemStatus.NotFound : CleanItemStatus.Failed, CleanFailure.Describe(ex));
                        continue;
                    }
                    var (ok, kind, reason) = FileUtil.DeleteFileEx(item.Path);
                    ctx.Report(item, ok ? CleanItemStatus.Success
                        : kind == FileUtil.DeleteFailReason.Locked ? CleanItemStatus.SkippedLocked
                        : CleanItemStatus.Failed, reason);
                }
            }
            FlushQuarantine();
        }, ctx.Ct);
    }

    // ————— 扫描辅助 —————

    protected static void AddDirFiles(RuleResult res, ScanContext ctx, string dir, string pattern, bool recursive, long minSize = 0)
    {
        if (!PathUtil.IsLocalPath(dir) || !Directory.Exists(dir) || PathUtil.HasReparseAncestor(dir) ||
            ctx.ExcludedPaths.Any(e => !string.IsNullOrWhiteSpace(e) && PathUtil.IsUnder(dir, e))) return;
        ctx.Detail?.Invoke(dir);
        var files = recursive ? SafeWalk(dir, ctx) : SafeFiles(dir, pattern, ctx);
        foreach (var f in files)
        {
            ctx.Ct.ThrowIfCancellationRequested();
            try
            {
                if (PathUtil.IsReparsePoint(f) ||
                    ctx.ExcludedPaths.Any(e => !string.IsNullOrWhiteSpace(e) && PathUtil.IsUnder(f, e))) continue;
                if (pattern != "*")
                {
                    var name = System.IO.Path.GetFileName(f);
                    if (!LikeMatch(name, pattern)) continue;
                }
                var fi = new FileInfo(f);
                if (minSize > 0 && fi.Length < minSize) continue;
                if (ctx.SkipRecentMinutes > 0 &&
                    (DateTime.Now - fi.LastWriteTime).TotalMinutes < ctx.SkipRecentMinutes) continue;
                if (ctx.Whitelist.Count > 0 && IsWhitelisted(f, ctx)) { continue; }
                res.Items.Add(f, fi.Length, fi.LastWriteTime);
                res.FileCount++;
                res.TotalSize += fi.Length;
            }
            catch { }
        }
    }

    private static bool IsWhitelisted(string path, ScanContext ctx)
    {
        foreach (var w in ctx.Whitelist)
            if (!string.IsNullOrWhiteSpace(w) && PathUtil.IsUnder(path, w)) return true;
        return false;
    }

    protected static void AddWholeDirItem(RuleResult res, string dir)
    {
        try
        {
            if (!PathUtil.IsLocalPath(dir) || PathUtil.HasReparseAncestor(dir)) return;
            if (!Directory.Exists(dir)) return;
            var size = FileUtil.DirSize(dir);
            if (size == 0) return;
            res.Items.Add(new CleanItem { Path = dir, Size = size, IsDirectory = true, ModifiedTime = SafeDirTime(dir) });
            res.FileCount++;
            res.TotalSize += size;
        }
        catch { }
    }

    private static DateTime SafeDirTime(string d)
    {
        try { return Directory.GetLastWriteTime(d); } catch { return DateTime.Now; }
    }

    private static IEnumerable<string> SafeFiles(string dir, string pattern, ScanContext ctx)
    {
        return SafeEntries(dir, pattern, directories: false, ctx);
    }

    private static IEnumerable<string> SafeEntries(string dir, string pattern, bool directories, ScanContext ctx)
    {
        IEnumerator<string> iterator;
        try { iterator = (directories ? Directory.EnumerateDirectories(dir) : Directory.EnumerateFiles(dir, pattern)).GetEnumerator(); }
        catch { yield break; }
        using (iterator)
        {
            while (true)
            {
                ctx.Ct.ThrowIfCancellationRequested();
                bool next;
                try { next = iterator.MoveNext(); }
                catch { yield break; }
                if (!next) yield break;
                yield return iterator.Current;
            }
        }
    }

    /// <summary>安全递归遍历：不进入 Reparse Point、排除目录、深度限制。</summary>
    protected static IEnumerable<string> SafeWalk(string root, ScanContext ctx, int maxDepth = 12)
    {
        return Walk(root, 0);
        IEnumerable<string> Walk(string dir, int depth)
        {
            ctx.Ct.ThrowIfCancellationRequested();
            if (PathUtil.IsReparsePoint(dir) || IsWhitelisted(dir, ctx) ||
                ctx.ExcludedPaths.Any(e => !string.IsNullOrWhiteSpace(e) && PathUtil.IsUnder(dir, e))) yield break;
            ctx.Detail?.Invoke(dir);
            foreach (var f in SafeFiles(dir, "*", ctx)) yield return f;
            if (depth >= Math.Clamp(maxDepth, 0, 64)) yield break;
            foreach (var d in SafeEntries(dir, "*", directories: true, ctx))
                foreach (var file in Walk(d, depth + 1)) yield return file;
        }
    }

    protected static bool LikeMatch(string text, string pattern)
    {
        // 简单通配符 * 匹配（不区分大小写）
        return LikeImpl(text.ToLowerInvariant(), pattern.ToLowerInvariant());
        static bool LikeImpl(string t, string p)
        {
            int ti = 0, pi = 0, star = -1, mark = 0;
            while (ti < t.Length)
            {
                if (pi < p.Length && (p[pi] == '?' || p[pi] == t[ti])) { ti++; pi++; }
                else if (pi < p.Length && p[pi] == '*') { star = pi++; mark = ti; }
                else if (star >= 0) { pi = star + 1; ti = ++mark; }
                else return false;
            }
            while (pi < p.Length && p[pi] == '*') pi++;
            return pi == p.Length;
        }
    }

    protected static string Exp(params string[] parts)
    {
        var p = System.IO.Path.Combine(parts);
        return Environment.ExpandEnvironmentVariables(p);
    }

    protected static bool ProcRunning(params string[] names) => ProcessUtil.AnyRunning(names);
}

public static class ProcessUtil
{
    public static bool AnyRunning(params string[] names)
        => AnyRunning(out _, names);

    public static bool AnyRunning(out List<string> found, params string[] names)
    {
        found = new List<string>();
        try
        {
            var set = names.Select(n => n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? n[..^4] : n)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var p in Process.GetProcesses())
            {
                using var process = p;
                try
                {
                    if (set.Contains(p.ProcessName.ToLowerInvariant()) && !found.Contains(p.ProcessName + ".exe"))
                        found.Add(p.ProcessName + ".exe");
                }
                catch { }
            }
        }
        catch { }
        return found.Count > 0;
    }
}
