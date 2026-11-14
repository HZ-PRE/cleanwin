using System.Collections;
using System.Diagnostics;
using CleanOptimize.Core.Models;
using CleanOptimize.Core.Store;
using CleanOptimize.Core.Utils;

namespace CleanOptimize.Core.Rules;

public sealed class CleanRequestRule
{
    public required IScanRule Rule { get; init; }
    public required RuleResult Result { get; init; }
    public required IReadOnlyList<CleanItem> Selected { get; init; }
}

/// <summary>Accurate aggregate counts, bounded diagnostic samples, and throttled UI notifications.</summary>
public sealed class CleanService
{
    internal const int DetailLimit = 1000;
    public async Task<CleanSummary> CleanAsync(IReadOnlyList<CleanRequestRule> requests,
        CancellationToken ct, IProgress<CleanProgress>? progress)
    {
        var summary = new CleanSummary();
        var settings = SettingsService.Current;
        var whitelist = new HashSet<string>(settings.Whitelist, StringComparer.OrdinalIgnoreCase);
        bool useQuarantine = settings.QuarantineEnabled;
        int total = requests.Sum(r => r.Selected.Count), processed = 0;
        string path = "", ruleName = "";
        var clock = Stopwatch.StartNew();
        long lastReport = -100;
        void Notify(bool force = false)
        {
            if (progress == null || (!force && clock.ElapsedMilliseconds - lastReport < 100)) return;
            lastReport = clock.ElapsedMilliseconds;
            progress.Report(new CleanProgress { CurrentPath = path, RuleName = ruleName,
                Processed = processed, Total = total, FreedBytes = summary.FreedBytes, QuarantinedBytes = summary.QuarantinedBytes });
        }

        foreach (var req in requests)
        {
            if (ct.IsCancellationRequested) { summary.Cancelled = true; break; }
            var items = new IndexedItems(req.Selected);
            var reported = new BitArray(items.Count);
            ruleName = req.Rule.Name;
            summary.RequestedBytes += req.Selected.Sum(i => i.Size);
            void Report(CleanItem item, CleanItemStatus status, string note)
            {
                int index = item.OperationIndex;
                if ((uint)index >= (uint)items.Count) throw new InvalidOperationException("规则返回了不属于本次请求的项目");
                if (reported[index]) return;
                reported[index] = true;
                processed++; path = item.Path;
                if (status != CleanItemStatus.Success) summary.FreedBytes += Math.Max(0, item.DeletedBytes);
                switch (status)
                {
                    case CleanItemStatus.Success: summary.SuccessCount++; summary.FreedBytes += Math.Max(0, item.Size); break;
                    case CleanItemStatus.Quarantined: summary.QuarantineCount++; summary.QuarantinedBytes += Math.Max(0, item.Size); break;
                    case CleanItemStatus.Failed: summary.FailCount++; break;
                    default: summary.SkipCount++; break;
                }
                if (summary.Details.Count < DetailLimit)
                    summary.Details.Add(new CleanItemOutcome { Path = item.Path, Status = status, Size = item.Size, Note = note });
                else summary.OmittedDetailCount++;
                Notify();
            }
            var context = new CleanContext
            {
                Result = req.Result, Selected = items, UseQuarantine = useQuarantine && req.Rule.CanRestore,
                Ct = ct, Progress = null, Whitelist = whitelist, Report = Report,
            };
            context.AllowedRoots.AddRange(req.Rule.AllowedRoots());
            try { await req.Rule.CleanAsync(context); }
            catch (OperationCanceledException) { summary.Cancelled = true; break; }
            catch (Exception ex)
            {
                // A bit per selected item replaces an ever-growing path set and the old quadratic lookup.
                for (int i = 0; i < items.Count; i++)
                    if (!reported[i]) Report(items[i], CleanItemStatus.Failed, ex.Message);
            }
        }
        if (ct.IsCancellationRequested) summary.Cancelled = true;
        Notify(force: true); // Final state must arrive even for very short operations or cancellation.
        summary.Duration = DateTime.Now - summary.StartedAt;
        var title = string.Format(CoreLocalizer.T("history.clean.title", "删除了 {0}"), SizeText.OfBytes(summary.FreedBytes));
        var detail = string.Format(CoreLocalizer.T("history.clean.detail", "成功 {0}，进隔离区 {1}，跳过 {2}，失败 {3}"),
            summary.SuccessCount, summary.QuarantineCount, summary.SkipCount, summary.FailCount);
        if (summary.QuarantineCount > 0)
            detail += "；" + string.Format(CoreLocalizer.T("ui.clean.quarantined", "另有 {0} 移入隔离区，不计入释放空间。"), SizeText.OfBytes(summary.QuarantinedBytes));
        HistoryService.Add(summary.Cancelled ? "CleanCancelled" : "Clean", title, detail, summary.FreedBytes);
        return summary;
    }

    private sealed class IndexedItems(IReadOnlyList<CleanItem> source) : IReadOnlyList<CleanItem>
    {
        public int Count => source.Count;
        public CleanItem this[int index]
        {
            get
            {
                var item = source[index];
                if (source is not CleanItemCollection)
                    item = new CleanItem { Path = item.Path, Size = item.Size, ModifiedTime = item.ModifiedTime,
                        IsDirectory = item.IsDirectory, Note = item.Note, Risk = item.Risk };
                item.OperationIndex = index;
                return item;
            }
        }
        public IEnumerator<CleanItem> GetEnumerator() { for (int i = 0; i < Count; i++) yield return this[i]; }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
