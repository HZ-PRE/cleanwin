using System.Windows;
using System.Windows.Input;
using CleanOptimize.App.Services;

namespace CleanOptimize.App.Windows;

public partial class UpdateWindow : Window
{
    private readonly MainWindow _main;
    private readonly UpdateResult _release;
    private CancellationTokenSource? _download;
    private bool _closed;
    private readonly Func<UpdateResult, IProgress<long>, CancellationToken, Task<DownloadedUpdate>> _downloadFile;
    private readonly Func<DownloadedUpdate, CancellationToken, Task> _install;

    internal UpdateWindow(MainWindow main, UpdateResult release,
        Func<UpdateResult, IProgress<long>, CancellationToken, Task<DownloadedUpdate>>? download = null,
        Func<DownloadedUpdate, CancellationToken, Task>? install = null)
    {
        InitializeComponent();
        _main = main; _release = release;
        _downloadFile = download ?? UpdateService.DownloadAsync;
        _install = install ?? UpdateService.StartInstallerAsync;
        if (main.IsLoaded) Owner = main;
        VersionMessage.Text = string.Format(Loc.T("ui.update.versions"), UpdateService.CurrentVersion, release.Version);
        Closed += (_, _) => { _closed = true; _download?.Cancel(); };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } };
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private async void Confirm_Click(object sender, RoutedEventArgs e) => await ConfirmAsync();
    internal async Task ConfirmAsync()
    {
        if (_download != null || _closed || !_main.CanInteract) return;
        if (_main.HasActiveOperations) { StatusText.SetResourceReference(System.Windows.Controls.TextBlock.TextProperty, "ui.update.busy"); return; }
        using var cancellation = new CancellationTokenSource();
        _download = cancellation;
        ConfirmButton.IsEnabled = false;
        DownloadProgress.Visibility = Visibility.Visible;
        DownloadProgress.Value = 0;
        StatusText.SetResourceReference(System.Windows.Controls.TextBlock.TextProperty, "ui.update.downloading");
        bool acceptingProgress = true;
        try
        {
            var progress = new Progress<long>(bytes =>
            {
                if (!acceptingProgress || _closed || cancellation.IsCancellationRequested) return;
                DownloadProgress.Value = _release.InstallerBytes > 0 ? Math.Clamp(bytes * 100.0 / _release.InstallerBytes, 0, 100) : 0;
                StatusText.Text = string.Format(Loc.T("ui.update.progress"), Core.Utils.SizeText.OfBytes(bytes), Core.Utils.SizeText.OfBytes(_release.InstallerBytes));
            });
            var downloaded = await _downloadFile(_release, progress, cancellation.Token);
            acceptingProgress = false;
            if (_closed || !_main.CanInteract) return;
            if (_main.HasActiveOperations) { StatusText.SetResourceReference(System.Windows.Controls.TextBlock.TextProperty, "ui.update.busy"); return; }
            StatusText.SetResourceReference(System.Windows.Controls.TextBlock.TextProperty, "ui.update.installing");
            await _install(downloaded, cancellation.Token);
            if (_closed) return;
            Close();
            _main.ForceClose();
        }
        catch (OperationCanceledException)
        {
            if (!_closed) StatusText.SetResourceReference(System.Windows.Controls.TextBlock.TextProperty, "ui.update.download.failed");
        }
        catch (Exception ex)
        {
            AppLog.Write("Update.Download", ex.GetType().Name);
            if (!_closed) StatusText.SetResourceReference(System.Windows.Controls.TextBlock.TextProperty, "ui.update.download.failed");
        }
        finally
        {
            acceptingProgress = false;
            _download = null;
            if (!_closed) ConfirmButton.IsEnabled = true;
        }
    }
}
