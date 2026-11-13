using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CleanOptimize.Core.Utils;

namespace CleanOptimize.App.Services;

internal static class AppIconService
{
    private const int Capacity = 192;
    private static readonly SemaphoreSlim Workers = new(2);
    private static readonly Dictionary<(string, int), ImageSource> Cache = new();
    private static readonly Queue<(string, int)> Order = new();
    internal static ImageSource Fallback { get; } = MakeFallback();
    internal static ImageSource CachedOrFallback(string path, int index)
    {
        lock (Cache) return Cache.GetValueOrDefault((path.ToUpperInvariant(), index)) ?? Fallback;
    }

    internal static async Task<ImageSource> GetAsync(string path, int index, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(path)) return Fallback;
        var key = (path.ToUpperInvariant(), index);
        lock (Cache) { if (Cache.TryGetValue(key, out var cached)) return cached; }
        await Workers.WaitAsync(token);
        try
        {
            lock (Cache) { if (Cache.TryGetValue(key, out var cached)) return cached; }
            var image = await Task.Run(() => ReadLocal(path, index), token);
            token.ThrowIfCancellationRequested();
            lock (Cache)
            {
                if (!Cache.ContainsKey(key))
                {
                    while (Cache.Count >= Capacity) Cache.Remove(Order.Dequeue());
                    Cache.Add(key, image); Order.Enqueue(key);
                }
            }
            return image;
        }
        finally { Workers.Release(); }
    }

    private static ImageSource ReadLocal(string path, int index)
    {
        try
        {
            if (!PathUtil.IsLocalPath(path) || PathUtil.HasReparseAncestor(path) || !File.Exists(path)) return Fallback;
            var extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension is ".png" or ".ico")
            {
                if (new FileInfo(path).Length > 16 * 1024 * 1024) return Fallback;
                using var stream = File.OpenRead(path);
                var bitmap = new BitmapImage();
                bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.DecodePixelWidth = 32; bitmap.StreamSource = stream; bitmap.EndInit(); bitmap.Freeze();
                return bitmap;
            }
            if (extension is not (".exe" or ".dll")) return Fallback;
            IntPtr large = IntPtr.Zero, small = IntPtr.Zero;
            try
            {
                ExtractIconExW(path, index, out large, out small, 1);
                var handle = large != IntPtr.Zero ? large : small;
                if (handle == IntPtr.Zero) return Fallback;
                var source = Imaging.CreateBitmapSourceFromHIcon(handle, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(32, 32));
                source.Freeze(); return source;
            }
            finally
            {
                if (large != IntPtr.Zero) DestroyIcon(large);
                if (small != IntPtr.Zero && small != large) DestroyIcon(small);
            }
        }
        catch { return Fallback; }
    }

    private static ImageSource MakeFallback()
    {
        var group = new DrawingGroup();
        using (var dc = group.Open())
        {
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(231, 239, 253)), null, new Rect(0, 0, 32, 32), 7, 7);
            var brush = new SolidColorBrush(Color.FromRgb(53, 117, 232));
            foreach (var x in new[] { 8, 18 }) foreach (var y in new[] { 8, 18 })
                dc.DrawRoundedRectangle(brush, null, new Rect(x, y, 6, 6), 1, 1);
        }
        var image = new DrawingImage(group); image.Freeze(); return image;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint ExtractIconExW(string file, int index, out IntPtr large, out IntPtr small, uint count);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);
}
