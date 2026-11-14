using System.IO;
using CleanOptimize.Core.Models;
using CleanOptimize.Core.Store;
using CleanOptimize.Core.Utils;

namespace CleanOptimize.App.Services;

/// <summary>Only aggregate data: no file scan, process enumeration or icon loading on the home page.</summary>
internal sealed class HomeDashboardSnapshot
{
    internal sealed record Disk(string Name, long Total, long Free)
    {
        public long Used => Math.Max(0, Total - Free);
        public double UsedPercent => Total > 0 ? Math.Clamp(Used * 100.0 / Total, 0, 100) : 0;
    }

    public List<Disk> Drives { get; init; } = [];
    public LastScanSummary? Scan { get; init; }
    public DiskSummary? Analysis { get; init; }
    public string SelectedDrive { get; init; } = "";
    public int? AppCount { get; init; }
    public int? StartupCount { get; init; }
    public int? HighImpact { get; init; }
    public double? SystemFreePercent { get; init; }
    public long RecycleBytes { get; init; }
    public List<ActivityRecord> History { get; init; } = [];

    internal static string SystemDrive => Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";

    internal static HomeDashboardSnapshot Load(string selected)
    {
        var drives = new List<Disk>();
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (d.DriveType == DriveType.Fixed && d.IsReady)
                    drives.Add(new Disk(d.Name, d.TotalSize, d.AvailableFreeSpace));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        var system = drives.Find(d => string.Equals(d.Name, SystemDrive, StringComparison.OrdinalIgnoreCase));
        var current = drives.Find(d => string.Equals(d.Name, selected, StringComparison.OrdinalIgnoreCase)) ?? system ?? drives.FirstOrDefault();
        int? apps = null, startups = null, high = null;
        try { apps = EnumerationCache.GetApps().Count; } catch { }
        try
        {
            var items = EnumerationCache.GetStartup();
            startups = items.Count(i => i.Enabled && i.CanToggle);
            high = items.Count(i => i.Enabled && i.Impact == "高");
        }
        catch { }
        long bin = 0;
        try { (bin, _) = ShellUtil.QueryRecycleBin(); } catch { }
        return new HomeDashboardSnapshot
        {
            Drives = drives, SelectedDrive = current?.Name ?? "", Scan = ScanCache.Load(),
            Analysis = current == null ? null : DiskCache.Load(current.Name),
            AppCount = apps, StartupCount = startups, HighImpact = high,
            SystemFreePercent = system is { Total: > 0 } ? 100 - system.UsedPercent : null,
            RecycleBytes = bin, History = HistoryService.LoadAll(),
        };
    }
}
