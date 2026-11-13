using CleanOptimize.Core.Utils;

namespace CleanOptimize.Core.Security;

/// <summary>
/// 统一删除保护：所有清理/粉碎操作必须经过本类校验，禁止业务代码直接 File.Delete。
/// 校验策略：
///  1. 目标必须位于规则声明的允许根目录之下；
///  2. 目标绝不能位于用户文档目录与系统关键目录（硬编码拒绝表）；
///  3. 白名单优先级最高。
/// </summary>
public static class SafetyGuard
{
    /// <summary>硬编码拒绝清理的目录（不允许被任何通配规则直接删除）。</summary>
    private static readonly string[] UserProtectedDirs =
    {
        Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
        Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
    };

    /// <summary>程序自身安装目录（防止自删）。</summary>
    public static string AppInstallDir { get; set; } =
        AppContext.BaseDirectory;

    private static readonly string[] SystemDirsRelative =
    {
        "System32", "SysWOW64", "WinSxS", "Installer", "DriverStore", "Fonts",
        "registration", "servicing", "Microsoft.NET", "assembly",
    };

    private static readonly char[] Sep = ['\\', '/'];

    /// <summary>拒绝表命中判断（供测试与 UI 提示）。</summary>
    public static string? FindDenyReason(string path) => FindDenyReason(path, false);

    private static string? FindDenyReason(string path, bool explicitFileRecycle)
    {
        if (string.IsNullOrWhiteSpace(path)) return "空路径";
        if (!PathUtil.IsLocalPath(path)) return "只允许本地磁盘绝对路径";
        string full;
        try { full = System.IO.Path.GetFullPath(Environment.ExpandEnvironmentVariables(path)).TrimEnd('\\'); }
        catch { return "非法路径"; }

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var dir in explicitFileRecycle ? Array.Empty<string>() : UserProtectedDirs)
        {
            if (string.IsNullOrEmpty(dir)) continue;
            var d = dir.TrimEnd('\\');
            // 用户主目录本身，或用户目录的直接子目录（Documents/Downloads 等）受保护；
            // 但 AppData 下的内容不受此表保护（由规则允许根目录约束）。
            if (string.Equals(full, d, StringComparison.OrdinalIgnoreCase))
                return string.Equals(full, userProfile, StringComparison.OrdinalIgnoreCase) ? "用户主目录受保护" : "用户文档目录受保护";
            if (full.Length > d.Length && full.StartsWith(d + "\\", StringComparison.OrdinalIgnoreCase))
            {
                // AppData 白名单例外：AppData 由各规则的允许根约束
                var rest = full[(d.Length + 1)..].Split(Sep, 2)[0];
                if (!rest.Equals("AppData", StringComparison.OrdinalIgnoreCase))
                    return "用户文档目录受保护";
            }
        }

        var windir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (string.Equals(full, windir, StringComparison.OrdinalIgnoreCase)) return "Windows 根目录受保护";
        if (full.StartsWith(windir + "\\", StringComparison.OrdinalIgnoreCase))
        {
            var firstSeg = full[(windir.Length + 1)..].Split(Sep)[0];
            if (SystemDirsRelative.Contains(firstSeg, StringComparer.OrdinalIgnoreCase))
                return "Windows 关键目录受保护";
        }

        foreach (var pf in new[] {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) })
        {
            if (!string.IsNullOrEmpty(pf) &&
                (string.Equals(full, pf, StringComparison.OrdinalIgnoreCase) ||
                 full.StartsWith(pf + "\\", StringComparison.OrdinalIgnoreCase)))
                return "程序安装目录受保护（请使用应用管理卸载）";
        }

        if (full.EndsWith(":\\")) return "磁盘根目录受保护";
        if (full.Length <= 3 && full.EndsWith(':')) return "磁盘根目录受保护";

        if (AppInstallDir.TrimEnd('\\') != "" &&
            (string.Equals(full, AppInstallDir.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) ||
             full.StartsWith(AppInstallDir.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)))
            return "应用自身目录受保护";

        return null;
    }

    /// <summary>
    /// 清理校验：目标必须在规则允许根内、不在拒绝表、不在白名单。
    /// 返回 null 表示通过，否则返回拒绝原因。
    /// </summary>
    public static string? ValidateForClean(string path, IEnumerable<string> allowedRoots, ISet<string>? whitelist = null)
        => Validate(path, allowedRoots, whitelist, false);

    /// <summary>仅供用户确认后的单文件回收。自动清理、粉碎及目录删除不得调用。</summary>
    public static string? ValidateForExplicitFileRecycle(string path, ISet<string>? whitelist = null)
    {
        var reason = Validate(path, new[] { path }, whitelist, true);
        if (reason != null) return reason;
        if (!File.Exists(path)) return "文件不存在或目标不是普通文件";
        return null;
    }

    /// <summary>仅供用户明确选择且确认的粉碎目标；不可用于自动清理。</summary>
    public static string? ValidateForExplicitShred(string path, string selectedRoot, ISet<string>? whitelist = null)
        => ValidateManualSelection(path, selectedRoot, whitelist);

    /// <summary>仅验证用户选择的空目录边界；调用方还必须重新确认目录树为空。</summary>
    public static string? ValidateForEmptyFolder(string path, ISet<string>? whitelist = null)
    {
        var reason = ValidateManualSelection(path, path, whitelist);
        return reason ?? (Directory.Exists(path) ? null : "目录不存在");
    }

    private static string? ValidateManualSelection(string path, string selectedRoot, ISet<string>? whitelist)
    {
        var reason = Validate(path, new[] { selectedRoot }, whitelist, true);
        if (reason != null) return reason;
        // Never allow selecting a whole profile, special folder or their ancestor.
        foreach (var dir in UserProtectedDirs.Where(d => !string.IsNullOrWhiteSpace(d)))
            if (PathUtil.IsUnder(dir, path)) return "不能处理整个用户主目录或文档目录";
        return File.Exists(path) || Directory.Exists(path) ? null : "目标不存在";
    }

    private static string? Validate(string path, IEnumerable<string> allowedRoots, ISet<string>? whitelist, bool explicitFileRecycle)
    {
        var deny = FindDenyReason(path, explicitFileRecycle);
        if (deny != null) return deny;

        string full;
        try { full = System.IO.Path.GetFullPath(Environment.ExpandEnvironmentVariables(path)).TrimEnd('\\'); }
        catch { return "非法路径"; }

        bool inAllowed = false;
        foreach (var root in allowedRoots)
        {
            if (PathUtil.IsUnder(full, root)) { inAllowed = true; break; }
        }
        if (!inAllowed) return "目标不在该规则的允许目录内";

        // 扫描后目录可能被换成链接；执行删除前重新检查路径的每一级。
        for (string? current = full; !string.IsNullOrEmpty(current); current = System.IO.Path.GetDirectoryName(current))
        {
            if (PathUtil.IsReparsePoint(current)) return "目标路径包含目录链接或符号链接";
        }

        if (whitelist != null)
        {
            foreach (var w in whitelist)
            {
                if (string.IsNullOrWhiteSpace(w)) continue;
                if (PathUtil.IsUnder(full, w) ||
                    (Directory.Exists(full) && PathUtil.IsUnder(w, full))) return "白名单项目";
            }
        }
        return null;
    }
}
