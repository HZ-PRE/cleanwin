using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using CleanOptimize.Core.Utils;

namespace CleanOptimize.Core.Analysis;

public sealed class DuplicateFile
{
    public string Path { get; set; } = "";
    public long Size { get; set; }
    public DateTime ModifiedTime { get; set; }
    public bool Selected { get; set; }
}

public sealed class DuplicateGroup
{
    public string Hash { get; set; } = "";
    public long Size { get; set; }
    public List<DuplicateFile> Files { get; } = new();
    public long WastedBytes => Size * Math.Max(0, Files.Count - 1);
}

public sealed class DuplicateScanResult
{
    public List<DuplicateGroup> Groups { get; } = new();
    public long TotalWastedBytes => Groups.Sum(g => g.WastedBytes);
    public int ScannedFiles { get; set; }
    public TimeSpan Duration { get; set; }
}

/// <summary>
/// 重复文件检测：大小分组 → 首 64KB 快速哈希 → 全量 SHA256。
/// 绝不通过文件名/日期判断重复。
/// </summary>
public sealed class DuplicateFileService
{
    public async Task<DuplicateScanResult> ScanAsync(
        IReadOnlyList<string> roots,
        long minFileSize,
        CancellationToken ct,
        IProgress<(string dir, int files)>? progress)
    {
        return await Task.Run(() =>
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var result = new DuplicateScanResult();
            var bySize = new Dictionary<long, List<string>>();
            int scanned = 0;
            var settings = Store.SettingsService.Current;
            var whitelist = new HashSet<string>(settings.Whitelist, StringComparer.OrdinalIgnoreCase);
            var excluded = settings.ExcludedPaths.ToList();
            // 在根目录层去重，避免为每个文件再保存一份 HashSet 索引。
            var scanRoots = new List<string>();
            foreach (var root in roots.Where(PathUtil.IsLocalPath).Select(PathUtil.Normalize)
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p => p.Length))
                if (!scanRoots.Any(parent => PathUtil.IsUnder(root, parent))) scanRoots.Add(root);

            // 1) 大小分组
            foreach (var root in scanRoots)
            {
                if (!PathUtil.IsLocalPath(root) || PathUtil.HasReparseAncestor(root) || !Directory.Exists(root)) continue;
                foreach (var f in Walk(root, whitelist, excluded, ct))
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        var fi = new FileInfo(f);
                        if (fi.Length < minFileSize) continue;
                        if (!bySize.TryGetValue(fi.Length, out var files)) bySize[fi.Length] = files = new List<string>();
                        files.Add(f);
                        scanned++;
                        if (scanned % 500 == 0) progress?.Report((f, scanned));
                    }
                    catch { }
                }
            }

            // 2) 快速哈希 → 3) 全量哈希
            int threads = Math.Clamp(settings.ScanThreads, 2, 16);
            var candidates = bySize.Where(kv => kv.Value.Count > 1 && kv.Key >= minFileSize).ToList();
            var confirmed = new ConcurrentBag<(string hash, long size, List<string> files)>();

            Parallel.ForEach(candidates, new ParallelOptions { MaxDegreeOfParallelism = threads, CancellationToken = ct },
                kv =>
                {
                    ct.ThrowIfCancellationRequested();
                    var fast = new Dictionary<string, List<string>>();
                    foreach (var f in kv.Value)
                    {
                        ct.ThrowIfCancellationRequested();
                        try
                        {
                            var h = FileUtil.Sha256OfFile(f, partialOnly: true, ct: ct);
                            if (!fast.TryGetValue(h, out var l)) fast[h] = l = new List<string>();
                            l.Add(f);
                        }
                        catch (OperationCanceledException) { throw; }
                        catch { }
                    }
                    foreach (var (prefixHash, group) in fast.Where(pair => pair.Value.Count > 1))
                    {
                        // 小文件的前缀哈希已覆盖完整内容，省去第二次打开及读取。
                        if (kv.Key <= 64 * 1024)
                        {
                            confirmed.Add((prefixHash, kv.Key, group.OrderBy(File.GetLastWriteTime).ToList()));
                            continue;
                        }
                        var full = new Dictionary<string, List<string>>();
                        foreach (var f in group)
                        {
                            try
                            {
                                var h = FileUtil.Sha256OfFile(f, partialOnly: false, ct: ct);
                                if (!full.TryGetValue(h, out var l)) full[h] = l = new List<string>();
                                l.Add(f);
                            }
                            catch (OperationCanceledException) { throw; }
                            catch { }
                        }
                        foreach (var (hash, files) in full.Where(l => l.Value.Count > 1))
                        {
                            confirmed.Add((hash, kv.Key, files.OrderBy(f => File.GetLastWriteTime(f)).ToList()));
                        }
                    }
                });

            foreach (var (hash, size, files) in confirmed)
            {
                var grp = new DuplicateGroup { Hash = hash, Size = size };
                foreach (var f in files)
                {
                    try
                    {
                        grp.Files.Add(new DuplicateFile
                        {
                            Path = f,
                            Size = size,
                            ModifiedTime = File.GetLastWriteTime(f),
                        });
                    }
                    catch { }
                }
                result.Groups.Add(grp);
            }
            result.Groups.Sort((a, b) => b.WastedBytes.CompareTo(a.WastedBytes));
            result.ScannedFiles = scanned;
            result.Duration = sw.Elapsed;
            return result;
        }, ct);
    }

    /// <summary>自动选择策略：每组保留最新/最旧，其余标记为删除候选。</summary>
    public static void AutoSelect(IEnumerable<DuplicateGroup> groups, bool keepNewest)
    {
        foreach (var g in groups)
        {
            var ordered = keepNewest
                ? g.Files.OrderByDescending(f => f.ModifiedTime).ToList()
                : g.Files.OrderBy(f => f.ModifiedTime).ToList();
            for (int i = 0; i < ordered.Count; i++)
                ordered[i].Selected = i > 0; // 保留第一个，其余选中
        }
    }

    public static void ClearSelection(IEnumerable<DuplicateGroup> groups)
    {
        foreach (var g in groups)
            foreach (var f in g.Files)
                f.Selected = false;
    }

    private static IEnumerable<string> Walk(string root, HashSet<string> whitelist, List<string> excluded, CancellationToken ct)
    {
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var dir = stack.Pop();
            if (PathUtil.IsReparsePoint(dir) ||
                whitelist.Any(w => !string.IsNullOrWhiteSpace(w) && PathUtil.IsUnder(dir, w)) ||
                excluded.Any(e => !string.IsNullOrWhiteSpace(e) && PathUtil.IsUnder(dir, e))) continue;
            string[] files = Array.Empty<string>(), dirs = Array.Empty<string>();
            try { files = Directory.GetFiles(dir); dirs = Directory.GetDirectories(dir); }
            catch { continue; }
            foreach (var f in files)
            {
                ct.ThrowIfCancellationRequested();
                if (PathUtil.IsReparsePoint(f) || excluded.Any(e => !string.IsNullOrWhiteSpace(e) && PathUtil.IsUnder(f, e))) continue;
                bool wl = false;
                foreach (var w in whitelist) if (!string.IsNullOrWhiteSpace(w) && PathUtil.IsUnder(f, w)) { wl = true; break; }
                if (!wl) yield return f;
            }
            foreach (var d in dirs)
            {
                if (PathUtil.IsReparsePoint(d)) continue;
                bool ex = false;
                foreach (var e in excluded) if (PathUtil.IsUnder(d, e)) { ex = true; break; }
                if (!ex) stack.Push(d);
            }
        }
    }
}
