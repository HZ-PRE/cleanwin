using System.Windows;
using System.Windows.Controls;
using CleanOptimize.App.Services;

namespace CleanOptimize.App.Controls;

internal sealed class AppIcon : Image
{
    private CancellationTokenSource? _pending;
    public AppIcon(string path, int index)
    {
        Width = Height = 32;
        Source = AppIconService.CachedOrFallback(path, index);
        Loaded += async (_, _) =>
        {
            _pending?.Cancel();
            using var pending = new CancellationTokenSource();
            _pending = pending;
            try
            {
                var image = await AppIconService.GetAsync(path, index, pending.Token);
                if (!pending.IsCancellationRequested && IsLoaded) Source = image;
            }
            catch (OperationCanceledException) { }
            finally { if (ReferenceEquals(_pending, pending)) _pending = null; }
        };
        Unloaded += (_, _) => _pending?.Cancel();
    }
}
