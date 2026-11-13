using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using CleanOptimize.Core.Utils;

namespace CleanOptimize.Core.SelfTest;

/// <summary>Removes only exact, owned fixtures after exercising the real Windows recycle operation.</summary>
public sealed class RecycleTestScope : IDisposable
{
    private readonly Dictionary<string, string?> _owned = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _before = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _bins = new(StringComparer.OrdinalIgnoreCase);

    public void Track(string path)
    {
        path = Path.GetFullPath(path);
        if (!PathUtil.IsLocalPath(path) || PathUtil.HasReparseAncestor(path)) throw new IOException("Invalid test fixture");
        string? hash = null;
        if (Directory.Exists(path)) AssertEmptyTree(path);
        else hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        var bin = Path.Combine(Path.GetPathRoot(path)!, "$Recycle.Bin", WindowsIdentity.GetCurrent().User!.Value);
        if (_bins.Add(bin) && Directory.Exists(bin))
            foreach (var entry in Directory.EnumerateFiles(bin, "$I*")) _before.Add(entry);
        _owned.Add(path, hash);
    }

    public void Dispose()
    {
        foreach (var bin in _bins)
        {
            if (!Directory.Exists(bin)) continue;
            if (PathUtil.HasReparseAncestor(bin)) throw new IOException("Recycle directory is a link");
            foreach (var metadata in Directory.EnumerateFiles(bin, "$I*"))
            {
                if (_before.Contains(metadata) || PathUtil.IsReparsePoint(metadata)) continue;
                var original = ReadOriginalPath(metadata);
                if (original == null || !_owned.TryGetValue(original, out var hash)) continue;
                var payload = Path.Combine(bin, "$R" + Path.GetFileName(metadata)[2..]);
                if (PathUtil.HasReparseAncestor(payload)) throw new IOException("Recycled fixture is a link");
                if (hash == null)
                {
                    AssertEmptyTree(payload);
                    Directory.Delete(payload, true);
                }
                else
                {
                    if (new FileInfo(payload).Length > 1024 * 1024 ||
                        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(payload))) != hash)
                        throw new IOException("Recycled fixture content changed");
                    File.Delete(payload);
                }
                File.Delete(metadata);
                if (File.Exists(metadata) || File.Exists(payload) || Directory.Exists(payload))
                    throw new IOException("Recycled test fixture was not removed");
            }
        }
    }

    private static string? ReadOriginalPath(string metadata)
    {
        var length = new FileInfo(metadata).Length;
        if (length < 28 || length > 65536) return null;
        var bytes = File.ReadAllBytes(metadata);
        var version = BitConverter.ToInt64(bytes, 0);
        if (version is not (1 or 2)) return null;
        var offset = version == 2 ? 28 : 24;
        if ((bytes.Length - offset) % 2 != 0) return null;
        return Encoding.Unicode.GetString(bytes, offset, bytes.Length - offset).TrimEnd('\0');
    }

    private static void AssertEmptyTree(string path)
    {
        if (!Directory.Exists(path) || PathUtil.HasReparseAncestor(path)) throw new IOException("Invalid empty fixture");
        foreach (var child in Directory.EnumerateFileSystemEntries(path))
        {
            if (PathUtil.IsReparsePoint(child) || !Directory.Exists(child)) throw new IOException("Fixture is no longer empty");
            AssertEmptyTree(child);
        }
    }
}
