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
    public bool Pending { get; set; }
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
    private string? _loadedDirectory;
    internal long ManifestBytesWritten { get; private set; }
    internal int ManifestWriteCount { get; private set; }

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
        if (_entries != null && _loadedDirectory == SettingsService.DataDir) return;
        try
        {
            if (PathUtil.HasReparseAncestor(ManifestFile) || PathUtil.HasReparseAncestor(Dir)) throw new IOException("隔离清单不能是链接");
            List<QuarantineEntry> entries;
            if (File.Exists(ManifestFile))
            {
                using var stream = File.OpenRead(ManifestFile);
                entries = JsonSerializer.Deserialize<List<QuarantineEntry>>(stream) ?? new();
            }
            else entries = new();
            // A durable intent without a payload means the move never completed. Never touch its source.
            entries.RemoveAll(entry => entry.Pending && !PendingPayloadExists(entry));
            foreach (var entry in entries) entry.Pending = false;
            _entries = entries;
            _loadedDirectory = SettingsService.DataDir;
        }
        catch (Exception ex) { throw new InvalidDataException("隔离清单无法读取，已停止操作以保护恢复记录", ex); }
    }

    private static bool PendingPayloadExists(QuarantineEntry entry)
    {
        if (entry.StoredName != entry.Id.ToString("N") + ".bin") throw new InvalidDataException("隔离记录路径无效");
        var path = Path.Combine(Dir, entry.StoredName);
        if (PathUtil.IsReparsePoint(path)) throw new InvalidDataException("隔离记录不能引用链接");
        return File.Exists(path) || Directory.Exists(path);
    }

    private void Persist()
    {
        Directory.CreateDirectory(SettingsService.DataDir);
        if (PathUtil.HasReparseAncestor(ManifestFile)) throw new IOException("隔离清单不能是链接");
        var temporary = ManifestFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, _entries, JsonOpt);
                stream.Flush(flushToDisk: true);
                ManifestBytesWritten += stream.Length;
                ManifestWriteCount++;
            }
            File.Move(temporary, ManifestFile, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    /// <summary>把文件/目录移动到隔离区。返回是否成功。</summary>
    public bool MoveIntoQuarantine(string path, string ruleId, out string? error)
    {
        bool moved = false;
        string? failure = null;
        MoveBatchIntoQuarantine(new[] { new CleanItem { Path = path } }, ruleId, CancellationToken.None, null,
            (_, success, reason) => { moved = success; failure = reason; });
        error = failure;
        return moved;
    }

    internal void MoveBatchIntoQuarantine(IReadOnlyList<CleanItem> items, string ruleId, CancellationToken ct,
        Func<CleanItem, string?>? validate, Action<CleanItem, bool, string?> report)
    {
        if (items.Count > 64) throw new ArgumentOutOfRangeException(nameof(items));
        lock (Lock)
        {
            var prepared = new List<(CleanItem Item, QuarantineEntry Entry)>();
            try
            {
                Load();
                Directory.CreateDirectory(Dir);
                foreach (var item in items)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        ValidateMove(item, validate);
                        var isDir = Directory.Exists(item.Path);
                        var id = Guid.NewGuid();
                        var entry = new QuarantineEntry
                        {
                            Id = id, OriginalPath = Path.GetFullPath(item.Path), StoredName = id.ToString("N") + ".bin",
                            Size = isDir ? FileUtil.DirSize(item.Path) : new FileInfo(item.Path).Length,
                            RuleId = ruleId, CreatedAt = DateTime.Now,
                            ExpireAt = DateTime.Now.AddDays(Math.Max(1, SettingsService.Current.QuarantineDays)),
                            IsDirectory = isDir, Pending = true,
                        };
                        prepared.Add((item, entry));
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException) { report(item, false, ex.Message); }
                }
                if (prepared.Count == 0) return;
                _entries!.AddRange(prepared.Select(p => p.Entry));
                try { Persist(); } // Atomic, flushed recovery intents for the whole bounded batch, before any move.
                catch
                {
                    foreach (var (_, entry) in prepared) _entries.Remove(entry);
                    throw;
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                foreach (var item in prepared.Count > 0 ? prepared.Select(p => p.Item) : items) report(item, false, ex.Message);
                return;
            }
            try
            {
                foreach (var (item, entry) in prepared)
                {
                    ct.ThrowIfCancellationRequested();
                    string? error = null;
                    try
                    {
                        ValidateMove(item, validate); // Recheck protection and links immediately before the move.
                        var target = Path.Combine(Dir, entry.StoredName);
                        if (entry.IsDirectory) Directory.Move(entry.OriginalPath, target);
                        else File.Move(entry.OriginalPath, target);
                        entry.Pending = false;
                        item.Size = entry.Size;
                    }
                    catch (Exception ex) { error = ex.Message; }
                    report(item, !entry.Pending, error);
                }
            }
            finally
            {
                foreach (var (_, entry) in prepared) if (entry.Pending) _entries!.Remove(entry);
                // If this checkpoint fails, the pre-move intents remain durable and Load reconciles payloads.
                try { Persist(); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
    }

    private static void ValidateMove(CleanItem item, Func<CleanItem, string?>? validate)
    {
        if (!PathUtil.IsLocalPath(item.Path) || PathUtil.HasReparseAncestor(item.Path) || PathUtil.HasReparseAncestor(Dir))
            throw new IOException("不允许隔离远程路径或目录链接");
        var deny = validate?.Invoke(item);
        if (deny != null) throw new IOException(deny);
        if (!File.Exists(item.Path) && !Directory.Exists(item.Path)) throw new FileNotFoundException("文件不存在");
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
