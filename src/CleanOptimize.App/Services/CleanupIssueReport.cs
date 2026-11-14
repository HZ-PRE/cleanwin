using CleanOptimize.Core.Models;

namespace CleanOptimize.App.Services;

internal sealed class CleanupIssueReport
{
    internal int Total { get; }
    internal CleanItemOutcome[] Items { get; }
    internal string Summary => string.Format(Loc.T("clean.issues.summary"), Total, Items.Length);
    internal CleanupIssueReport(CleanSummary result)
    {
        Total = result.FailCount + result.SkipCount;
        Items = result.Details.Where(i => i.Status is not (CleanItemStatus.Success or CleanItemStatus.Quarantined)).Take(1000).ToArray();
    }

    internal static string Reason(CleanItemOutcome item) => item.Status switch
    {
        CleanItemStatus.SkippedLocked => Loc.T("clean.reason.locked"),
        CleanItemStatus.NotFound => Loc.T("clean.reason.missing"),
        CleanItemStatus.SkippedWhitelist => Loc.T("clean.reason.protected") + (string.IsNullOrWhiteSpace(item.Note) ? "" : "\n" + item.Note),
        _ => string.IsNullOrWhiteSpace(item.Note) ? Loc.T("clean.reason.unknown") : item.Note,
    };

    internal void Show(MainWindow owner)
    {
        if (owner.CanInteract && Total > 0) new Windows.CleanupIssuesWindow(owner, this).ShowDialog();
    }
}
