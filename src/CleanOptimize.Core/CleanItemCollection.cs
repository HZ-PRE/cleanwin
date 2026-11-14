using System.Collections;

namespace CleanOptimize.Core.Models;

/// <summary>Compact scan snapshots; shared directory prefixes and small blocks avoid large reference arrays.</summary>
public sealed class CleanItemCollection : IReadOnlyList<CleanItem>
{
    private const int BlockSize = 512;
    private readonly List<Entry[]> _blocks = new();
    private readonly Dictionary<string, string> _prefixes = new(StringComparer.Ordinal);
    private string? _lastPrefix;
    private readonly record struct Entry(string? Prefix, string Path, long Size, DateTime Modified,
        string Note, RiskLevel Risk, bool Directory);
    public int Count { get; private set; }

    public void Add(CleanItem item)
        => Add(item.Path, item.Size, item.ModifiedTime, item.IsDirectory, item.Note, item.Risk);

    public void Add(string path, long size, DateTime modified, bool directory = false, string note = "", RiskLevel risk = RiskLevel.Safe)
    {
        string? prefix = null;
        int split = Math.Max(path.LastIndexOf('\\'), path.LastIndexOf('/')) + 1;
        if (split > 0 && split < path.Length)
        {
            if (_lastPrefix is { } last && split == last.Length && path.AsSpan(0, split).SequenceEqual(last.AsSpan()))
            {
                prefix = last; path = path[split..];
            }
            else
            {
                var candidate = path[..split];
                if (_prefixes.TryGetValue(candidate, out prefix)) path = path[split..];
                else if (_prefixes.Count < 4096)
                {
                    prefix = candidate; _prefixes.Add(prefix, prefix); path = path[split..];
                }
            }
            _lastPrefix = prefix;
        }
        if (Count % BlockSize == 0) _blocks.Add(new Entry[BlockSize]);
        _blocks[Count / BlockSize][Count % BlockSize] = new(prefix, path, size, modified, note, risk, directory);
        Count++;
    }

    public CleanItem this[int index]
    {
        get
        {
            if ((uint)index >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));
            var e = _blocks[index / BlockSize][index % BlockSize];
            return new CleanItem { Path = e.Prefix == null ? e.Path : e.Prefix + e.Path, Size = e.Size,
                ModifiedTime = e.Modified, Note = e.Note, Risk = e.Risk, IsDirectory = e.Directory };
        }
    }
    public IEnumerator<CleanItem> GetEnumerator() { for (int i = 0; i < Count; i++) yield return this[i]; }
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
