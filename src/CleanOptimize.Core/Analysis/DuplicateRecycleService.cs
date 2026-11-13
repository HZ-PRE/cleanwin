using System.Runtime.InteropServices;
using CleanOptimize.Core.Security;
using CleanOptimize.Core.Store;
using CleanOptimize.Core.Utils;

namespace CleanOptimize.Core.Analysis;

public sealed class DuplicateRecycleResult
{
    public List<string> RecycledPaths { get; } = new();
    public List<(string Path, string Reason)> Failures { get; } = new();
    public long RecycledBytes { get; internal set; }
}

/// <summary>User-confirmed duplicate files only. Never falls back to permanent deletion.</summary>
public static class DuplicateRecycleService
{
    public static Task<DuplicateRecycleResult> RecycleSelectedAsync(IEnumerable<DuplicateGroup> groups)
    {
        // Freeze selection before leaving the UI thread. Shell file operations require STA.
        var snapshot = groups.Select(g => (g.Hash,
            Selected: g.Files.Where(f => f.Selected).Select(f => (f.Path, f.Size)).ToArray(),
            Kept: g.Files.Where(f => !f.Selected).Select(f => f.Path).ToArray())).ToArray();
        var whitelist = SettingsService.Current.Whitelist.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var completion = new TaskCompletionSource<DuplicateRecycleResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var result = new DuplicateRecycleResult();
                var handled = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var group in snapshot)
                {
                    if (group.Selected.Length == 0) continue;
                    string? kept = null;
                    foreach (var path in group.Kept)
                    {
                        try
                        {
                            if (PathUtil.IsLocalPath(path) && !PathUtil.HasReparseAncestor(path) && File.Exists(path) &&
                                FileUtil.Sha256OfFile(path) == group.Hash) { kept = path; break; }
                        }
                        catch { /* Try another retained copy. */ }
                    }
                    foreach (var file in group.Selected)
                    {
                        if (!handled.Add(PathUtil.Normalize(file.Path))) continue;
                        string? error = kept == null || !File.Exists(kept) ? "没有可保留的有效副本，请重新扫描并至少保留一份" : null;
                        try
                        {
                            error ??= SafetyGuard.ValidateForExplicitFileRecycle(file.Path, whitelist);
                            if (error == null && (new FileInfo(file.Path).Length != file.Size || FileUtil.Sha256OfFile(file.Path) != group.Hash))
                                error = "文件在扫描后已改变，请重新扫描";
                            if (error == null) error = RecycleFile(file.Path, whitelist);
                        }
                        catch (Exception ex) { error = ex.Message; }
                        if (error != null) result.Failures.Add((file.Path, error));
                        else { result.RecycledPaths.Add(file.Path); result.RecycledBytes += file.Size; }
                    }
                }
                completion.SetResult(result);
            }
            catch (Exception ex) { completion.SetException(ex); }
        }) { IsBackground = true, Name = "Duplicate file recycle" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private static string? RecycleFile(string path, ISet<string> whitelist)
    {
        var reason = SafetyGuard.ValidateForExplicitFileRecycle(path, whitelist);
        if (reason != null) return reason;
        return RecycleValidatedPath(path);
    }

    // Shared native operation; callers must validate their specific file/directory policy immediately before calling.
    internal static string? RecycleValidatedPath(string path)
    {
        IFileOperation? operation = null;
        IntPtr item = IntPtr.Zero;
        try
        {
            operation = (IFileOperation)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("3AD05575-8857-4850-9277-11B85BDB8E09"))!)!;
            // RECYCLEONDELETE forbids a permanent-delete fallback. EARLYFAILURE returns errors without hidden dialogs.
            Marshal.ThrowExceptionForHR(operation.SetOperationFlags(0x00080000 | 0x00100000 | 0x20000000 | 0x10 | 0x400 | 0x4));
            var iid = new Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE");
            Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(path, IntPtr.Zero, ref iid, out item));
            Marshal.ThrowExceptionForHR(operation.DeleteItem(item, IntPtr.Zero));
            Marshal.ThrowExceptionForHR(operation.PerformOperations());
            Marshal.ThrowExceptionForHR(operation.GetAnyOperationsAborted(out bool aborted));
            return aborted || File.Exists(path) || Directory.Exists(path) ? "目标未移入回收站（操作取消、文件占用或此位置不支持回收）" : null;
        }
        finally
        {
            if (item != IntPtr.Zero) Marshal.Release(item);
            if (operation != null) Marshal.FinalReleaseComObject(operation);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(string path, IntPtr bindContext, ref Guid iid, out IntPtr item);

    // Keep all methods in native vtable order, including unused slots.
    [ComImport, Guid("947AAB5F-0A5C-4C13-B4D6-4BF7836FC9F8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileOperation
    {
        [PreserveSig] int Advise(IntPtr sink, out uint cookie);
        [PreserveSig] int Unadvise(uint cookie);
        [PreserveSig] int SetOperationFlags(uint flags);
        [PreserveSig] int SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string message);
        [PreserveSig] int SetProgressDialog(IntPtr dialog);
        [PreserveSig] int SetProperties(IntPtr properties);
        [PreserveSig] int SetOwnerWindow(IntPtr owner);
        [PreserveSig] int ApplyPropertiesToItem(IntPtr item);
        [PreserveSig] int ApplyPropertiesToItems(IntPtr items);
        [PreserveSig] int RenameItem(IntPtr item, IntPtr name, IntPtr sink);
        [PreserveSig] int RenameItems(IntPtr items, IntPtr name);
        [PreserveSig] int MoveItem(IntPtr item, IntPtr destination, IntPtr name, IntPtr sink);
        [PreserveSig] int MoveItems(IntPtr items, IntPtr destination);
        [PreserveSig] int CopyItem(IntPtr item, IntPtr destination, IntPtr name, IntPtr sink);
        [PreserveSig] int CopyItems(IntPtr items, IntPtr destination);
        [PreserveSig] int DeleteItem(IntPtr item, IntPtr sink);
        [PreserveSig] int DeleteItems(IntPtr items);
        [PreserveSig] int NewItem(IntPtr destination, uint attributes, IntPtr name, IntPtr template, IntPtr sink);
        [PreserveSig] int PerformOperations();
        [PreserveSig] int GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool aborted);
    }
}
