using System.IO;
using System.Text.Json;
using CleanOptimize.Core.Models;
using CleanOptimize.Core.Store;
using CleanOptimize.Core.Utils;

namespace CleanOptimize.Core.Rules;

public sealed class QuarantineEntry
{
    public Guid Id { get; set; }
    public string OriginalPath { get; set; } = "";
    public string StoredName { get; set; } = "";
    public long Size { get; set; }
    public string RuleId { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime ExpireAt { get; set; } = DateTime.Now.AddDays(30);
    public bool IsDirectory { get; set; }
}

public sealed class QuarantinePurgeResult
{
    public int DeletedCount { get; internal set; }
    public long DeletedBytes { get; internal set; }
    public List<(Guid Id, string Reason)> Failures { get; } = new();
}

/// <summary>
/// 清理隔离区：非纯缓存文件默认移入隔离区，保留 N 天可随时恢复，到期自动永久删除。
/// </summary>
public sealed class QuarantineService
{
    public static QuarantineService Instance { get; } = new();

    private static string Dir => SettingsService.QuarantineDir;
    private static string ManifestFile => Path.Combine(SettingsService.DataDir, "quarantine.json");

    private static readonly object Lock = new();
    private List<QuarantineEntry>? _entries;

    private static readonly JsonSerializerOptions JsonOpt = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public IReadOnlyList<QuarantineEntry> Entries
    {
        get { lock (Lock) { Load(); return _entries!.ToList(); } }
    }

    public long TotalBytes => Entries.Sum(e => e.Size);

    private void Load()
    {
        if (_entries != null) return;
        try
        {
            _entries = File.Exists(ManifestFile)
                ? JsonSerializer.Deserialize<List<QuarantineEntry>>(File.ReadAllText(ManifestFile)) ?? new()
                : new();
        }
        catch (Exception ex) { throw new InvalidDataException("隔离清单无法读取，已停止操作以保护恢复记录", ex); }
    }

    private void Persist()
    {
        Directory.CreateDirectory(SettingsService.DataDir);
        var temporary = ManifestFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(_entries, JsonOpt));
            File.Move(temporary, ManifestFile, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    /// <summary>把文件/目录移动到隔离区。返回是否成功。</summary>
    public bool MoveIntoQuarantine(string path, string ruleId, out string? error)
    {
        error = null;
        try
        {
            if (!PathUtil.IsLocalPath(path) || PathUtil.HasReparseAncestor(path) || PathUtil.HasReparseAncestor(Dir))
            { error = "不允许隔离远程路径或目录链接"; return false; }
            path = Path.GetFullPath(path);
            if (!File.Exists(path) && !Directory.Exists(path)) { error = CoreLocalizer.T("quar.err.notfound", "文件不存在"); return false; }
            Directory.CreateDirectory(Dir);

            long size = 0;
            var isDir = Directory.Exists(path);
            if (isDir) size = FileUtil.DirSize(path);
            else size = new FileInfo(path).Length;

            var id = Guid.NewGuid();
            var storedName = id.ToString("N") + ".bin";
            var target = Path.Combine(Dir, storedName);
            lock (Lock)
            {
                Load();
                var entry = new QuarantineEntry
                {
                    Id = id,
                    OriginalPath = path,
                    StoredName = storedName,
                    Size = size,
                    RuleId = ruleId,
                    CreatedAt = DateTime.Now,
                    ExpireAt = DateTime.Now.AddDays(Math.Max(1, SettingsService.Current.QuarantineDays)),
                    IsDirectory = isDir,
                };
                _entries!.Add(entry);
                try
                {
                    // 先保存恢复记录，再移动文件。断电或磁盘写入失败不会产生无记录的隔离文件。
                    Persist();
                    if (isDir) Directory.Move(path, target);
                    else File.Move(path, target);
                }
                catch
                {
                    _entries.Remove(entry);
                    try { Persist(); } catch { }
                    throw;
                }
            }
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message.Contains("另一个程序正使用") || ex.Message.Contains("being used")
                ? CoreLocalizer.T("quar.err.locked", "文件被占用")
                : ex.Message;
            return false;
        }
    }

    /// <summary>恢复到原始位置。原目录不存在会自动创建；重名自动加后缀。返回 null=成功，否则错误信息。</summary>
    public string? Restore(Guid id)
    {
        lock (Lock)
        {
            Load();
            var e = _entries!.FirstOrDefault(x => x.Id == id);
            if (e == null) return CoreLocalizer.T("quar.err.norecord", "记录不存在");
            if (e.StoredName != e.Id.ToString("N") + ".bin" || PathUtil.HasReparseAncestor(Dir) ||
                !PathUtil.IsLocalPath(e.OriginalPath) || PathUtil.HasReparseAncestor(e.OriginalPath)) return "隔离记录路径无效";
            var stored = Path.Combine(Dir, e.StoredName);
            if (PathUtil.IsReparsePoint(stored)) return "不能恢复链接项目";
            if (!File.Exists(stored) && !Directory.Exists(stored)) return CoreLocalizer.T("quar.err.missing", "隔离文件已丢失");

            try
            {
                var targetDir = System.IO.Path.GetDirectoryName(e.OriginalPath);
                if (!string.IsNullOrEmpty(targetDir)) Directory.CreateDirectory(targetDir);
                var target = e.OriginalPath;
                if (File.Exists(target) || Directory.Exists(target))
                {
                    var ext = System.IO.Path.GetExtension(target);
                    var stem = System.IO.Path.GetFileNameWithoutExtension(target);
                    var dir = System.IO.Path.GetDirectoryName(target);
                    var suffix = CoreLocalizer.T("quar.restore.suffix", "已恢复");
                    target = Path.Combine(dir ?? "", $"{stem} ({suffix}){ext}");
                    int n = 1;
                    while (File.Exists(target) || Directory.Exists(target))
                    {
                        target = Path.Combine(dir ?? "", $"{stem} ({suffix} {++n}){ext}");
                    }
                }
                if (e.IsDirectory) Directory.Move(stored, target);
                else File.Move(stored, target);

                _entries!.Remove(e);
                Persist();
                HistoryService.Add("Restore", CoreLocalizer.T("history.quar.restored", "恢复了 1 个隔离项目"), e.OriginalPath, e.Size);
                return null;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }
    }

    public bool DeletePermanent(Guid id, out long size)
        => DeletePermanent(id, out size, out _);

    public bool DeletePermanent(Guid id, out long size, out string? error)
    {
        size = 0;
        error = null;
        lock (Lock)
        {
            Load();
            var e = _entries!.FirstOrDefault(x => x.Id == id);
            if (e == null) { error = "记录不存在"; return false; }
            if (!TryDeleteStored(e, out error)) return false;
            size = e.Size;
            _entries!.Remove(e);
            Persist();
            return true;
        }
    }

    /// <summary>启动时调用：永久删除到期项目。</summary>
    public void PurgeExpired()
    {
        lock (Lock)
        {
            Load();
            var expired = _entries!.Where(e => e.ExpireAt <= DateTime.Now).ToList();
            foreach (var e in expired)
            {
                if (TryDeleteStored(e, out _)) _entries!.Remove(e);
            }
            if (expired.Count > 0) Persist();
        }
    }

    public int PurgeAll()
        => PurgeAllDetailed().DeletedCount;

    public QuarantinePurgeResult PurgeAllDetailed()
    {
        lock (Lock)
        {
            Load();
            var result = new QuarantinePurgeResult();
            foreach (var e in _entries!.ToList())
            {
                if (TryDeleteStored(e, out var error))
                {
                    _entries!.Remove(e);
                    result.DeletedCount++;
                    result.DeletedBytes += e.Size;
                }
                else result.Failures.Add((e.Id, error ?? "删除失败"));
            }
            Persist();
            if (result.DeletedCount > 0) HistoryService.Add("QuarantinePurge", string.Format(CoreLocalizer.T("history.quar.purged", "清空了隔离区（{0} 个项目）"), result.DeletedCount));
            return result;
        }
    }

    private static bool TryDeleteStored(QuarantineEntry entry, out string? error)
    {
        error = null;
        try
        {
            // 清单仅允许引用本条目的 GUID 文件，拒绝绝对路径和路径穿越。
            if (entry.StoredName != entry.Id.ToString("N") + ".bin" || PathUtil.HasReparseAncestor(Dir))
            { error = "隔离记录路径无效"; return false; }
            var stored = Path.Combine(Dir, entry.StoredName);
            if (PathUtil.IsReparsePoint(stored)) { error = "隔离项目不能是链接"; return false; }
            if (Directory.Exists(stored))
            {
                string? failure = null;
                var result = FileUtil.DeleteDirectoryTree(stored, (_, reason) => failure ??= reason);
                if (result.fail == 0 && !Directory.Exists(stored)) return true;
                error = string.IsNullOrWhiteSpace(failure) ? "目录未能完全删除，可能包含占用文件" : failure;
                return false;
            }
            // Moving into quarantine preserves read-only attributes. Clear only that bit for deletion.
            if (File.Exists(stored))
            {
                var attributes = File.GetAttributes(stored);
                try
                {
                    if ((attributes & FileAttributes.ReadOnly) != 0) File.SetAttributes(stored, attributes & ~FileAttributes.ReadOnly);
                    File.Delete(stored);
                }
                catch
                {
                    try { if (File.Exists(stored)) File.SetAttributes(stored, attributes); } catch { }
                    throw;
                }
            }
            return true;
        }
        catch (Exception ex) { error = ex.Message; return false; }
    }
}
