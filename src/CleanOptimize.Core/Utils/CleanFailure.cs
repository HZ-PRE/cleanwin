using CleanOptimize.Core.Store;

namespace CleanOptimize.Core.Utils;

/// <summary>Explain Windows file errors without guessing from translated exception text.</summary>
public static class CleanFailure
{
    public static bool IsLocked(Exception error) => (error.HResult & 0xFFFF) is 32 or 33;

    public static string Describe(Exception error)
    {
        int code = error.HResult & 0xFFFF;
        if (IsLocked(error)) return CoreLocalizer.T("clean.reason.locked", "文件正在被其他程序使用，暂时无法清理。请关闭相关程序后重试；系统临时文件可在重启后再试。");
        if (error is UnauthorizedAccessException || code == 5)
            return CoreLocalizer.T("clean.reason.denied", "当前账户没有操作此文件的权限。可以尝试以管理员身份运行后重试；仍失败时请保留此文件，不要强行修改系统权限。");
        if (error is FileNotFoundException or DirectoryNotFoundException || code is 2 or 3)
            return CoreLocalizer.T("clean.reason.missing", "文件已被其他程序删除或移动，无需再次清理。重新扫描即可更新列表。");
        if (code is 39 or 112)
            return CoreLocalizer.T("clean.reason.space", "磁盘剩余空间不足，无法完成删除或移入隔离区。请先释放一些磁盘空间，再重试。");
        if (error is PathTooLongException || code == 206)
            return CoreLocalizer.T("clean.reason.path", "文件路径过长，当前操作无法处理。请在确认文件用途后手动处理，或暂时保留。");
        if (code == 145)
            return CoreLocalizer.T("clean.reason.directory", "文件夹中仍有未能删除的文件，或程序又生成了新文件。请关闭相关程序，重新扫描后再试。");
        if (error is InvalidDataException && error.Message.Contains("隔离", StringComparison.Ordinal))
            return CoreLocalizer.T("clean.reason.quarantine", "隔离区恢复记录无法读取。为避免丢失恢复信息，已停止清理；请先检查隔离区和本地日志。");
        return string.Format(CoreLocalizer.T("clean.reason.other", "系统未能完成操作，请稍后重试。技术信息：{0}"), error.Message);
    }
}
