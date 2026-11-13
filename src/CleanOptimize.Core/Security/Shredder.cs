using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32.SafeHandles;
using CleanOptimize.Core.Store;
using CleanOptimize.Core.Utils;

namespace CleanOptimize.Core.Security;

/// <summary>覆写仅限通过统一保护校验的本地普通文件；拒绝链接和硬链接。</summary>
public static class Shredder
{
    public static bool Shred(string path)
        => ShredCore(path, explicitSelection: false, out _);

    public static bool ShredSelected(string path, out string? error)
        => ShredCore(path, explicitSelection: true, out error);

    private static bool ShredCore(string path, bool explicitSelection, out string? error)
    {
        error = null;
        try
        {
            var whitelist = SettingsService.Current.Whitelist.ToHashSet(StringComparer.OrdinalIgnoreCase);
            string[] roots = [path];
            string? Validate(string target) => explicitSelection
                ? SafetyGuard.ValidateForExplicitShred(target, path, whitelist)
                : SafetyGuard.ValidateForClean(target, roots, whitelist);
            error = Validate(path);
            if (error != null) return false;
            if (File.Exists(path)) return ShredFile(path, Validate, out error);
            if (!Directory.Exists(path)) { error = "目标不存在"; return false; }
            var directories = new Stack<string>();
            var orderedDirs = new List<string>();
            var files = new List<string>();
            directories.Push(path);
            // 先检查整棵树；遇到受保护内容时不开始粉碎。
            while (directories.Count > 0)
            {
                var dir = directories.Pop();
                orderedDirs.Add(dir);
                foreach (var entry in Directory.EnumerateFileSystemEntries(dir))
                {
                    error = Validate(entry);
                    if (error != null) return false;
                    if (Directory.Exists(entry)) directories.Push(entry);
                    else files.Add(entry);
                }
            }
            bool all = true;
            foreach (var file in files)
                if (!ShredFile(file, Validate, out var failure)) { all = false; error ??= failure; }
            // 只移除已空的目录，绝不递归删除被跳过或运行中新增的文件。
            foreach (var dir in orderedDirs.AsEnumerable().Reverse())
            {
                try
                {
                    var deny = Validate(dir);
                    if (deny != null) { all = false; error ??= deny; continue; }
                    Directory.Delete(dir, recursive: false);
                }
                catch (Exception ex) { all = false; error ??= ex.Message; }
            }
            return all;
        }
        catch (Exception ex) { error = ex.Message; return false; }
    }

    private static bool ShredFile(string file, Func<string, string?> validate, out string? error)
    {
        error = null;
        try
        {
            error = validate(file);
            if (error != null) return false;
            using (var fs = new FileStream(file, FileMode.Open, FileAccess.Write, FileShare.None))
            {
                if (!GetFileInformationByHandle(fs.SafeFileHandle, out var info) || info.NumberOfLinks != 1)
                { error = "无法校验文件身份或文件具有多个硬链接"; return false; }
                var name = new System.Text.StringBuilder(32768);
                var length = GetFinalPathNameByHandleW(fs.SafeFileHandle, name, (uint)name.Capacity, 0);
                if (length == 0 || length >= name.Capacity) { error = "无法校验实际文件路径"; return false; }
                var finalPath = name.ToString();
                if (finalPath.StartsWith(@"\\?\")) finalPath = finalPath[4..];
                if (!string.Equals(Path.GetFullPath(file), finalPath, StringComparison.OrdinalIgnoreCase) ||
                    validate(finalPath) != null) { error = "实际文件路径校验失败"; return false; }
                var buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(64 * 1024);
                try
                {
                    for (int pass = 0; pass < 3; pass++)
                    {
                        fs.Position = 0;
                        long remaining = fs.Length;
                        while (remaining > 0)
                        {
                            int count = (int)Math.Min(64 * 1024, remaining);
                            if (pass == 1) buffer.AsSpan(0, count).Clear();
                            else RandomNumberGenerator.Fill(buffer.AsSpan(0, count));
                            fs.Write(buffer, 0, count);
                            remaining -= count;
                        }
                        fs.Flush(flushToDisk: true);
                    }
                    fs.SetLength(0);
                }
                finally { System.Buffers.ArrayPool<byte>.Shared.Return(buffer, clearArray: true); }
            }
            File.Delete(file);
            return true;
        }
        catch (Exception ex) { error = ex.Message; return false; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FileInformation
    {
        public uint Attributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime, LastAccessTime, LastWriteTime;
        public uint VolumeSerialNumber, FileSizeHigh, FileSizeLow, NumberOfLinks, FileIndexHigh, FileIndexLow;
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle file, out FileInformation information);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(SafeFileHandle file, System.Text.StringBuilder path, uint size, uint flags);
}
