using System.IO;
using CleanOptimize.Core.Utils;

namespace CleanOptimize.Core.Analysis;

public sealed class EmptyFolderInfo
{
    public string Path { get; set; } = "";
    public DateTime LastWriteTime { get; set; }
    /// <summary>目录树内的子目录数（含自身层级下的空子目录）。</summary>
    public int SubFolderCount { get; set; }
}

public sealed class EmptyFolderCleanResult
{
    public List<string> RecycledPaths { get; } = new();
    public List<(string Path, string Reason)> Failures { get; } = new();
}

/// <summary>
/// 空文件夹查找：递归判定"整棵子树不含任何文件"的目录。
/// 不跨越 Reparse Point，忽略常见系统/受保护目录；
/// 无法枚举（权限不足等）的目录一律视为非空，宁漏勿删。
/// </summary>
public sealed class EmptyFolderService
{
    public static Task<EmptyFolderCleanResult> RecycleSelectedAsync(IEnumerable<string> selected)
    {
        var paths = selected.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var whitelist = Store.SettingsService.Current.Whitelist.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var completion = new TaskCompletionSource<EmptyFolderCleanResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var result = new EmptyFolderCleanResult();
                foreach (var path in paths)
                {
                    string? error = null;
                    try
                    {
                        error = ValidateStillEmpty(path, whitelist);
                        if (error == null) error = DuplicateRecycleService.RecycleValidatedPath(path);
                    }
                    catch (Exception ex) { error = ex.Message; }
                    if (error == null) result.RecycledPaths.Add(path);
                    else result.Failures.Add((path, error));
                }
                completion.SetResult(result);
            }
            catch (Exception ex) { completion.SetException(ex); }
        }) { IsBackground = true, Name = "Empty folder recycle" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private static string? ValidateStillEmpty(string path, ISet<string> whitelist)
    {
        var pending = new Stack<string>();
        pending.Push(path);
        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            var deny = Security.SafetyGuard.ValidateForEmptyFolder(dir, whitelist);
            if (deny != null) return deny;
            foreach (var child in Directory.EnumerateFileSystemEntries(dir))
            {
                if (PathUtil.IsReparsePoint(child)) return "目录中包含链接，已跳过";
                if (!Directory.Exists(child)) return "目录已包含文件，请重新扫描";
                pending.Push(child);
            }
        }
        return null;
    }
    private static readonly string[] SkipDirNames =
    [
        "$Recycle.Bin", "System Volume Information", "$WinREAgent", "Config.Msi",
        "Windows.old", "Recovery", "PerfLogs", "AppData",
    ];

    /// <summary>扫描 roots 下的空目录。maxDepth 为相对根的层级上限。</summary>
    public async Task<List<EmptyFolderInfo>> ScanAsync(
        IReadOnlyList<string> roots, int maxDepth, CancellationToken ct,
        IProgress<string>? progress = null)
    {
        var found = new List<EmptyFolderInfo>();
        maxDepth = Math.Clamp(maxDepth, 0, 64);
        await Task.Run(() =>
        {
            foreach (var root in roots.Select(PathUtil.Normalize).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                ct.ThrowIfCancellationRequested();
                if (!PathUtil.IsLocalPath(root) || PathUtil.HasReparseAncestor(root) || !Directory.Exists(root)) continue;
                progress?.Report(root);
                Walk(root, 0, maxDepth, ct, found);
            }
        }, ct);
        // 只保留最顶层的空目录（父空目录已覆盖子树），避免重复计数
        var distinct = new List<EmptyFolderInfo>();
        var unique = found.GroupBy(f => PathUtil.Normalize(f.Path), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First()).ToList();
        var paths = unique.Select(f => PathUtil.Normalize(f.Path)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var f in unique)
        {
            ct.ThrowIfCancellationRequested();
            var parent = Path.GetDirectoryName(f.Path);
            while (!string.IsNullOrEmpty(parent) && !paths.Contains(PathUtil.Normalize(parent)))
                parent = Path.GetDirectoryName(parent);
            if (string.IsNullOrEmpty(parent)) distinct.Add(f);
        }
        return distinct;
    }

    /// <summary>判定目录子树；整棵子树无文件时记入 found 并返回 true。</summary>
    private static bool Walk(string dir, int depth, int maxDepth, CancellationToken ct, List<EmptyFolderInfo> found)
    {
        ct.ThrowIfCancellationRequested();

        string[] entries;
        try
        {
            entries = Directory.GetFileSystemEntries(dir);
        }
        catch
        {
            // 枚举失败（权限等）：视为非空，保守处理
            return false;
        }

        int childDirs = 0;
        bool allEmpty = true;
        int firstChild = found.Count;
        foreach (var entry in entries)
        {
            ct.ThrowIfCancellationRequested();
            // 未检查的子项不能被当成空目录；仍继续查找其他安全的空子目录。
            if (!Directory.Exists(entry) || PathUtil.IsReparsePoint(entry) || depth >= maxDepth ||
                SkipDirNames.Contains(Path.GetFileName(entry), StringComparer.OrdinalIgnoreCase))
            {
                allEmpty = false;
                continue;
            }
            childDirs++;
            if (!Walk(entry, depth + 1, maxDepth, ct, found)) allEmpty = false;
        }

        // 空目录 = 无文件且所有子目录（可判定的）都是空目录
        if (allEmpty)
        {
            int descendants = childDirs + found.Skip(firstChild).Sum(f => f.SubFolderCount);
            found.RemoveRange(firstChild, found.Count - firstChild);
            found.Add(new EmptyFolderInfo
            {
                Path = dir,
                LastWriteTime = TryGetLastWrite(dir),
                SubFolderCount = descendants,
            });
            return true;
        }
        return false;
    }

    private static DateTime TryGetLastWrite(string dir)
    {
        try { return Directory.GetLastWriteTime(dir); }
        catch { return default; }
    }
}
