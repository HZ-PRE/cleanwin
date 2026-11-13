using System.Windows;
using System.Windows.Threading;

namespace CleanOptimize.App.Services;

/// <summary>Coalesces typing without retaining worker tasks or refreshing hidden pages.</summary>
internal sealed class SearchDelay
{
    private readonly DispatcherTimer _timer;
    private readonly Action _action;
    public SearchDelay(FrameworkElement owner, Action action)
    {
        _action = action;
        _timer = new DispatcherTimer(DispatcherPriority.Background, owner.Dispatcher)
            { Interval = TimeSpan.FromMilliseconds(160) };
        _timer.Tick += (_, _) => { _timer.Stop(); _action(); };
        owner.Unloaded += (_, _) => _timer.Stop();
    }
    public void Restart() { _timer.Stop(); _timer.Start(); }
}
