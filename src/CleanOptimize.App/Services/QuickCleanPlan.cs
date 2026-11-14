using CleanOptimize.Core.Models;
using CleanOptimize.Core.Rules;

namespace CleanOptimize.App.Services;

internal sealed record QuickCleanRequest(string? RuleId = null);

internal static class QuickCleanPlan
{
    internal static bool IsEligible(IScanRule rule) => rule.DefaultSelected && rule.Risk <= RiskLevel.Low &&
        rule.Group is RuleGroup.SystemJunk or RuleGroup.BrowserCache or RuleGroup.AppCache;

    internal static List<IScanRule> SelectRules(IEnumerable<IScanRule> rules, QuickCleanRequest request) =>
        rules.Where(r => IsEligible(r) && (request.RuleId == null || r.Id == request.RuleId)).ToList();

    internal static void Open(MainWindow main, string? ruleId = null)
    {
        if (!main.CanInteract) return;
        if (main.HasActiveOperations || main.UpdateInProgress)
        {
            main.ShowToast(Loc.T("ui.quick.busy"), ToastType.Info);
            return;
        }
        main.Navigate(PageKey.SmartScan, new QuickCleanRequest(ruleId));
    }
}
