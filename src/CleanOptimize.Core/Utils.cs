using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace CleanOptimize.Core.Utils;

public static class SizeText
{
    public static string OfBytes(long bytes, int digits = 1)
    {
        double b = bytes;
        return bytes switch
        {
            < 1024 => $"{bytes} B",
            < 1024L * 1024 => $"{(b / 1024).ToString($"F{digits}")} KB",
            < 1024L * 1024 * 1024 => $"{(b / 1024 / 1024).ToString($"F{digits}")} MB",
            < 1024L * 1024 * 1024 * 1024 => $"{(b / 1024 / 1024 / 1024).ToString($"F{digits}")} GB",
            _ => $"{(b / 1024 / 1024 / 1024 / 1024).ToString($"F{digits}")} TB",
        };
    }

    /// <summary>首页卡片那种"约 X.X GB"风格。</summary>
    public static string Approx(long bytes) => "约 " + OfBytes(bytes);
}

public static class PathUtil
{
    /// <summary>仅接受本地磁盘绝对路径，避免 UNC/设备路径和网络映射盘触发外网访问。</summary>
    public static bool IsLocalPath(string path)
    {
        try
        {
            var expanded = Expand(path);
            if (!Path.IsPathFullyQualified(expanded) || expanded.StartsWith('\\') ||
                expanded.Length < 3 || expanded[1] != ':' || expanded[2] != '\\' && expanded[2] != '/' ||
                expanded.AsSpan(2).Contains(':')) return false;
            return new DriveInfo(Path.GetPathRoot(expanded)!).DriveType is DriveType.Fixed or DriveType.Removable or DriveType.Ram;
        }
        catch { return false; }
    }

    public static bool HasReparseAncestor(string path)
    {
        if (!IsLocalPath(path)) return true;
        for (string? current = Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            if (IsReparsePoint(current)) return true;
        return false;
    }

    public static string Expand(string p) => Environment.ExpandEnvironmentVariables(p);

    public static string Normalize(string p)
    {
        try
        {
            var full = System.IO.Path.GetFullPath(Expand(p)).TrimEnd('\\');
            return full.Length == 0 ? full : full;
        }
        catch
        {
            return Expand(p).TrimEnd('\\');
        }
    }

    public static bool IsUnder(string path, string root)
    {
        var p = Normalize(path);
        var r = Normalize(root);
        if (r.Length == 0) return false;
        if (string.Equals(p, r, StringComparison.OrdinalIgnoreCase)) return true;
        return p.Length > r.Length && p[r.Length] is '\\' or '/' &&
               p.StartsWith(r, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsReparsePoint(string path)
    {
        try
        {
            var attr = File.GetAttributes(path);
            return attr.HasFlag(FileAttributes.ReparsePoint);
        }
        catch
        {
            return false;
        }
    }
}

public static class ShellUtil
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCTW
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string pTo;
        public ushort fFlags;
        public int fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string lpszProgressTitle;
    }

    private const uint FO_DELETE = 3;
    private const ushort FOF_ALLOWUNDO = 0x40;
    private const ushort FOF_NOCONFIRMATION = 0x10;
    private const ushort FOF_NOERRORUI = 0x400;
    private const ushort FOF_SILENT = 0x4;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int SHFileOperationW(ref SHFILEOPSTRUCTW op);

    /// <summary>把文件/目录移入回收站（可恢复），返回是否成功。</summary>
    public static bool RecycleToBin(string path)
    {
        if (Security.SafetyGuard.ValidateForClean(path, new[] { path },
            Store.SettingsService.Current.Whitelist.ToHashSet(StringComparer.OrdinalIgnoreCase)) != null) return false;
        var op = new SHFILEOPSTRUCTW
        {
            wFunc = FO_DELETE,
            pFrom = path + "\0",
            fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_NOERRORUI | FOF_SILENT,
        };
        return SHFileOperationW(ref op) == 0 && op.fAnyOperationsAborted == 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SHQUERYRBINFO
    {
        public int cbSize;
        public long i64Size;
        public long i64NumItems;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHQueryRecycleBinW(string pszRootPath, ref SHQUERYRBINFO info);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBinW(IntPtr hwnd, string? pszRootPath, uint dwFlags);

    private const uint SHERB_NOCONFIRMATION = 1;
    private const uint SHERB_NOPROGRESSUI = 2;
    private const uint SHERB_NOSOUND = 4;

    public static (long Size, long Count) QueryRecycleBin(string? driveRoot = null)
    {
        var info = new SHQUERYRBINFO { cbSize = Marshal.SizeOf<SHQUERYRBINFO>() };
        var rc = SHQueryRecycleBinW(driveRoot ?? "", ref info);
        if (rc != 0) return (0, 0);
        return (info.i64Size, info.i64NumItems);
    }

    public static bool EmptyRecycleBin(string? driveRoot = null)
    {
        return SHEmptyRecycleBinW(IntPtr.Zero, driveRoot, SHERB_NOCONFIRMATION | SHERB_NOSOUND) == 0;
    }

    public static void ShowInExplorer(string path)
    {
        if (!PathUtil.IsLocalPath(path) || PathUtil.HasReparseAncestor(path)) return;
        if (File.Exists(path))
            Process.Start("explorer.exe", $"/select,\"{path}\"");
        else
            Process.Start("explorer.exe", path);
    }

    public static void OpenPath(string path)
    {
        if (!PathUtil.IsLocalPath(path) || PathUtil.HasReparseAncestor(path))
            throw new InvalidOperationException("只允许打开本地文件或目录");
        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    public static void OpenSystemTool(string name)
    {
        if (!new[] { "taskmgr.exe", "powercfg.cpl", "diskmgmt.msc", "devmgmt.msc", "control.exe", "msinfo32.exe", "rstrui.exe" }
            .Contains(name, StringComparer.OrdinalIgnoreCase)) throw new ArgumentException("不支持的系统工具", nameof(name));
        Process.Start(new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, name)) { UseShellExecute = true });
    }
}

public static class FileUtil
{
    public enum DeleteFailReason { None, Denied, Locked, Other }

    /// <summary>安全删除文件：不存在视为成功。kind 用于与语言无关的占用判断。</summary>
    public static (bool ok, DeleteFailReason kind, string reason) DeleteFileEx(string path)
    {
        try
        {
            if (!File.Exists(path)) return (true, DeleteFailReason.None, "");
            File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
            return (true, DeleteFailReason.None, "");
        }
        catch (UnauthorizedAccessException) { return (false, DeleteFailReason.Denied, CleanOptimize.Core.Store.CoreLocalizer.T("del.err.denied", "权限不足")); }
        catch (IOException) { return (false, DeleteFailReason.Locked, CleanOptimize.Core.Store.CoreLocalizer.T("del.err.locked", "文件被占用")); }
        catch (Exception ex) { return (false, DeleteFailReason.Other, ex.Message); }
    }

    /// <summary>兼容入口：只需文本原因的调用方使用。</summary>
    public static (bool ok, string reason) DeleteFile(string path)
    {
        var (ok, _, reason) = DeleteFileEx(path);
        return (ok, reason);
    }

    /// <summary>
    /// 安全递归删除目录。不跨越 Reparse Point 递归（仅删除链接本身），
    /// 单个子项失败时记录并继续。返回 (成功子项数, 失败数, 释放字节)。
    /// </summary>
    public static (int ok, int fail, long freed) DeleteDirectoryTree(string dir, Action<string, string>? onItemFailed = null)
    {
        int ok = 0, fail = 0;
        long freed = 0;
        try
        {
            if (PathUtil.IsReparsePoint(dir))
            {
                try { Directory.Delete(dir, false); ok++; }
                catch { fail++; onItemFailed?.Invoke(dir, CleanOptimize.Core.Store.CoreLocalizer.T("del.err.link", "无法删除链接")); }
                return (ok, fail, freed);
            }

            foreach (var entry in Directory.EnumerateFileSystemEntries(dir))
            {
                try
                {
                    if (PathUtil.IsReparsePoint(entry))
                    {
                        if (Directory.Exists(entry)) Directory.Delete(entry, false);
                        else File.Delete(entry);
                        ok++;
                    }
                    else if (Directory.Exists(entry))
                    {
                        var (o, f, fr) = DeleteDirectoryTree(entry, onItemFailed);
                        ok += o; fail += f; freed += fr;
                    }
                    else
                    {
                        long size = 0;
                        try { size = new FileInfo(entry).Length; } catch { }
                        var (good, _) = DeleteFile(entry);
                        if (good) { ok++; freed += size; }
                        else { fail++; onItemFailed?.Invoke(entry, ""); }
                    }
                }
                catch
                {
                    fail++;
                    onItemFailed?.Invoke(entry, "");
                }
            }

            try { Directory.Delete(dir, false); }
            catch { }
        }
        catch (Exception)
        {
            fail++;
        }
        return (ok, fail, freed);
    }

    /// <summary>只清空目录的直接与间接子项，保留目录本身（如 Windows\Temp）。</summary>
    public static (int ok, int fail, long freed) DeleteDirectoryChildren(string dir)
    {
        int ok = 0, fail = 0;
        long freed = 0;
        try
        {
            if (!Directory.Exists(dir)) return (0, 0, 0);
            if (PathUtil.HasReparseAncestor(dir)) return (0, 1, 0);
            foreach (var entry in Directory.EnumerateFileSystemEntries(dir))
            {
                if (PathUtil.IsReparsePoint(entry))
                {
                    try { if (Directory.Exists(entry)) Directory.Delete(entry, false); else File.Delete(entry); ok++; }
                    catch { fail++; }
                }
                else if (Directory.Exists(entry))
                {
                    var (o, f, fr) = DeleteDirectoryTree(entry);
                    ok += o; fail += f; freed += fr;
                }
                else
                {
                    long size = 0;
                    try { size = new FileInfo(entry).Length; } catch { }
                    var (good, _) = DeleteFile(entry);
                    if (good) { ok++; freed += size; } else fail++;
                }
            }
        }
        catch { }
        return (ok, fail, freed);
    }

    /// <summary>枚举目录下直接子项，出错返回空。</summary>
    public static IEnumerable<string> SafeEnumerateFiles(string dir) { try { return Directory.EnumerateFiles(dir); } catch { return Enumerable.Empty<string>(); } }
    public static IEnumerable<string> SafeEnumerateDirs(string dir) { try { return Directory.EnumerateDirectories(dir); } catch { return Enumerable.Empty<string>(); } }

    public static string Sha256OfFile(string path, bool partialOnly = false, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var fs = File.OpenRead(path);
        using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buf = System.Buffers.ArrayPool<byte>.Shared.Rent(64 * 1024);
        try
        {
            int remaining = partialOnly ? 64 * 1024 : int.MaxValue;
            while (remaining > 0)
            {
                ct.ThrowIfCancellationRequested();
                int read = fs.Read(buf, 0, Math.Min(64 * 1024, remaining));
                if (read == 0) break;
                md5.AppendData(buf, 0, read);
                if (partialOnly) remaining -= read;
            }
            return Convert.ToHexString(md5.GetHashAndReset());
        }
        finally { System.Buffers.ArrayPool<byte>.Shared.Return(buf, clearArray: true); }
    }

    public static long DirSize(string dir)
    {
        if (!PathUtil.IsLocalPath(dir) || PathUtil.HasReparseAncestor(dir)) return 0;
        long total = 0;
        try
        {
            var stack = new Stack<string>();
            stack.Push(dir);
            while (stack.Count > 0)
            {
                var d = stack.Pop();
                if (PathUtil.IsReparsePoint(d)) continue;
                foreach (var f in SafeEnumerateFiles(d))
                {
                    try { total += new FileInfo(f).Length; } catch { }
                }
                foreach (var sub in SafeEnumerateDirs(d))
                {
                    if (!PathUtil.IsReparsePoint(sub)) stack.Push(sub);
                }
            }
        }
        catch { }
        return total;
    }
}
