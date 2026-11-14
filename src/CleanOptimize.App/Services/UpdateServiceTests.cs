using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using CleanOptimize.App.Pages;
using CleanOptimize.App.Windows;

namespace CleanOptimize.App.Services;

internal static class UpdateServiceTests
{
    internal static UpdateResult NewRelease => new(UpdateOutcome.Available, "9.0.0", "v9.0.0", 1024);
    private static string Release(string version, bool draft = false, bool preview = false, bool installer = true) => JsonSerializer.Serialize(new
    {
        tag_name = "v" + version, draft, prerelease = preview,
        assets = new[] { new { name = installer ? $"CleanOptimizeSetup-{version}.exe" : "other.zip", state = "uploaded", size = 1024 }, new { name = "SHA256SUMS.txt", state = "uploaded", size = 200 } },
    });

    internal static void VersionsAndRequest()
    {
        foreach (var (release, current, expected) in new[]
        {
            ("3.0.14", "3.0.13", true), ("3.0.9", "3.0.13", false), ("3.0.13", "3.0.13", false),
            ("3.0.13", "3.0.13-preview", true), ("3.0.13", "3.0.13+hash", false), ("3.1.0", "3.0.13", true),
            ("0.0.2", "0.0.1", true), ("0.0.2", "0.0.2", false), ("0.0.2", "3.0.10", false),
        })
            if (UpdateService.ParseRelease(Release(release), current).HasUpdate != expected) throw new Exception("Version comparison failed");
        foreach (var data in new[] { Release("9.0.0", draft: true), Release("9.0.0", preview: true), Release("9.0.0-preview"), Release("9.0.0", installer: false), Release("../9.0.0"), "{}", "[]" })
            if (UpdateService.ParseRelease(data, "3.0.13").HasUpdate) throw new Exception("Invalid release accepted");
        if (UpdateService.ParseRelease(Release("9.0.0").Replace("SHA256SUMS.txt", "other.txt"), "3.0.13").HasUpdate) throw new Exception("Release without checksums accepted");
        foreach (var incomplete in new[]
        {
            Release("9.0.0", installer: false), Release("9.0.0").Replace("SHA256SUMS.txt", "other.txt"),
            Release("9.0.0").Replace("\"size\":200", "\"size\":0"),
            Release("9.0.0").Replace("\"size\":200", "\"size\":\"invalid\""),
            Release("9.0.0").Replace("\"size\":200", "\"size\":262145"),
            Release("9.0.0").Replace("uploaded", "new"),
        })
            if (UpdateService.ParseRelease(incomplete, "3.0.13").Outcome != UpdateOutcome.IncompleteRelease)
                throw new Exception("Incomplete release was not distinguished from no release");
        using var request = UpdateService.CreateRequest();
        if (request.Method.Method != "GET" || request.RequestUri?.AbsoluteUri != UpdateService.Endpoint || request.Content != null || request.Headers.Authorization != null || request.Headers.Contains("Cookie") || request.Headers.CacheControl?.NoCache != true)
            throw new Exception("Update request must only read public metadata");
        if (!UpdateService.AllowedDownloadUri(UpdateService.AssetUri(NewRelease, false)) ||
            UpdateService.AllowedDownloadUri(new UriBuilder { Scheme = Uri.UriSchemeHttp, Host = "github.com" }.Uri) ||
            UpdateService.AllowedDownloadUri(new UriBuilder { Scheme = Uri.UriSchemeHttps, Host = "github.com.evil.invalid" }.Uri) ||
            UpdateService.AllowedDownloadUri(new UriBuilder { Scheme = Uri.UriSchemeHttps, Host = "github.com", UserName = "user" }.Uri))
            throw new Exception("Download host boundary failed");
    }

    internal static async Task RequestLifecycle()
    {
        var now = DateTimeOffset.UtcNow;
        int requests = 0;
        var ready = new TaskCompletionSource<ReleaseResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var service = new UpdateService(_ => { Interlocked.Increment(ref requests); return ready.Task; }, "3.0.13", () => now);
        var first = service.CheckAsync(); var second = service.CheckAsync();
        if (!ReferenceEquals(first, second)) throw new Exception("Requests were not coalesced");
        if (!ReferenceEquals(first, service.CheckAsync(forceRefresh: true))) throw new Exception("Manual check duplicated an in-flight request");
        ready.SetResult(new ReleaseResponse(200, Release("9.0.0")));
        if (!(await first).HasUpdate || !(await service.CheckAsync()).HasUpdate || requests != 1) throw new Exception("Update cache failed");
        int sequence = 0;
        service = new UpdateService(_ => Task.FromResult(++sequence == 1 ? new ReleaseResponse(200, Release("9.0.0")) : new ReleaseResponse(403, "")), "3.0.13", () => now);
        await service.CheckAsync(); now = now.AddMinutes(6);
        var failed = await service.CheckAsync();
        if (failed.Outcome != UpdateOutcome.Failed || !failed.HasUpdate) throw new Exception("Network failure erased known release");
        foreach (int code in new[] { 404, 403, 429, 500, 302 })
        {
            service = new UpdateService(_ => Task.FromResult(new ReleaseResponse(code, "")), "3.0.13", () => now);
            if ((await service.CheckAsync()).HasUpdate) throw new Exception("HTTP error fabricated update");
        }
        service = new UpdateService(_ => Task.FromResult(new ReleaseResponse(200, "invalid")), "3.0.13", () => now);
        if ((await service.CheckAsync()).Outcome != UpdateOutcome.Failed) throw new Exception("Bad JSON not handled");

        // Reproduce startup before publication, followed immediately by a manual check.
        foreach (int initialStatus in new[] { 404, 403, 500 })
        {
            requests = 0;
            service = new UpdateService(_ => Task.FromResult(++requests == 1
                ? new ReleaseResponse(initialStatus, "") : new ReleaseResponse(200, Release("0.0.2"))), "0.0.1", () => now);
            await service.CheckAsync();
            if ((await service.CheckAsync()).HasUpdate || requests != 1) throw new Exception("Background cache failed");
            var refreshed = await service.CheckAsync(forceRefresh: true);
            if (!refreshed.HasUpdate || refreshed.Version != "0.0.2" || requests != 2) throw new Exception("Manual check reused stale result");
        }

        requests = 0;
        service = new UpdateService(_ => Task.FromResult(++requests == 1 ? new ReleaseResponse(404, "") : new ReleaseResponse(200, Release("0.0.2"))), "0.0.1", () => now);
        var main = new MainWindow(service);
        try
        {
            await main.CheckForUpdatesAsync();
            var page = new SettingsPage(main);
            ((Button)page.FindName("BtnCheckUpdate")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await main.CheckForUpdatesAsync();
            if (requests != 2 || !main.UpdateState.HasUpdate) throw new Exception("Settings button did not refresh update data");
            QuickActions.CheckUpdate(main);
            await main.CheckForUpdatesAsync();
            if (requests != 3 || !main.UpdateState.HasUpdate) throw new Exception("Quick action did not refresh update data");
        }
        finally { main.ForceClose(); }
    }

    internal static async Task DownloadIntegrity()
    {
        byte[] bytes = new byte[150000]; new Random(13).NextBytes(bytes);
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        using var output = new MemoryStream();
        await UpdateService.CopyVerifiedAsync(new MemoryStream(bytes), output, bytes.Length, hash, null, CancellationToken.None);
        if (!output.ToArray().SequenceEqual(bytes)) throw new Exception("Verified copy differs");
        async Task Reject(Func<Task> operation)
        {
            try { await operation(); } catch (Exception ex) when (ex is InvalidDataException or OperationCanceledException) { return; }
            throw new Exception("Unsafe download accepted");
        }
        await Reject(() => UpdateService.CopyVerifiedAsync(new MemoryStream(bytes), new MemoryStream(), bytes.Length, new string('0', 64), null, CancellationToken.None));
        await Reject(() => UpdateService.CopyVerifiedAsync(new MemoryStream(bytes), new MemoryStream(), bytes.Length - 1, hash, null, CancellationToken.None));
        await Reject(() => UpdateService.CopyVerifiedAsync(new MemoryStream(bytes), new MemoryStream(), bytes.Length + 1, hash, null, CancellationToken.None));
        await Reject(() => UpdateService.CopyVerifiedAsync(new MemoryStream(bytes), new MemoryStream(), bytes.Length, hash, null, new CancellationToken(true)));
        await Reject(async () => { await UpdateService.ReadBodyAsync(new MemoryStream(new byte[UpdateService.MaxResponseBytes + 1]), CancellationToken.None); });
        var entry = $"{hash}  CleanOptimizeSetup-9.0.0.exe";
        if (UpdateService.ParseChecksum("\uFEFF" + entry, "CleanOptimizeSetup-9.0.0.exe") != hash) throw new Exception("Checksum parsing failed");
        await Reject(() => { UpdateService.ParseChecksum(entry + "\n" + entry, "CleanOptimizeSetup-9.0.0.exe"); return Task.CompletedTask; });
        await Reject(() => { UpdateService.AssetUri(NewRelease with { Tag = "../9.0.0" }, false); return Task.CompletedTask; });
        var directory = Path.Combine(Core.Store.SettingsService.DataDir, "update-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, "payload.bin");
        try
        {
            using (var source = new MemoryStream(bytes))
                await UpdateService.SaveVerifiedAsync(source, target, bytes.Length, hash, null, CancellationToken.None);
            if (!File.ReadAllBytes(target).SequenceEqual(bytes) || File.Exists(target + ".part")) throw new Exception("Downloaded file promotion failed");
            File.Delete(target);
            using (var source = new MemoryStream(bytes))
                await Reject(() => UpdateService.SaveVerifiedAsync(source, target, bytes.Length, new string('0', 64), null, CancellationToken.None));
            if (Directory.EnumerateFileSystemEntries(directory).Any()) throw new Exception("Corrupt download left files behind");
            using var cancellation = new CancellationTokenSource();
            using (var source = new MemoryStream(bytes))
                await Reject(() => UpdateService.SaveVerifiedAsync(source, target, bytes.Length, hash, new CancelProgress(cancellation), cancellation.Token));
            if (Directory.EnumerateFileSystemEntries(directory).Any()) throw new Exception("Cancelled download left files behind");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private sealed class CancelProgress(CancellationTokenSource cancellation) : IProgress<long>
    {
        public void Report(long value) => cancellation.Cancel();
    }

    internal static void UserInterface()
    {
        foreach (var theme in new[] { "light", "dark" })
        foreach (var (language, _) in Loc.Supported)
        {
            ThemeManager.Apply(theme); Loc.SetLanguage(language);
            var main = new MainWindow();
            try
            {
                var badge = (FrameworkElement)main.FindName("NewUpdateBadge");
                if (badge.Visibility != Visibility.Collapsed) throw new Exception("NEW shown before checking");
                main.ApplyUpdateResult(NewRelease);
                var page = new SettingsPage(main); page.SelectSection("about");
                foreach (int width in new[] { 660, 760 })
                {
                    page.Measure(new System.Windows.Size(width, 620)); page.Arrange(new Rect(0, 0, width, 620)); page.UpdateLayout();
                    foreach (string name in new[] { "BtnCheckUpdate", "UpdateAvailableBadge" })
                    {
                        var control = (FrameworkElement)page.FindName(name);
                        var pos = control.TransformToAncestor(page).Transform(new Point());
                        if (control.Visibility != Visibility.Visible || pos.X < 0 || pos.X + control.ActualWidth > width) throw new Exception("Update label out of bounds");
                    }
                }
                var shell = (FrameworkElement)main.Content;
                shell.Measure(new System.Windows.Size(900, 600)); shell.Arrange(new Rect(0, 0, 900, 600)); shell.UpdateLayout();
                var badgePosition = badge.TransformToAncestor(shell).Transform(new Point());
                if (badge.Visibility != Visibility.Visible || badgePosition.X + badge.ActualWidth > 184) throw new Exception("NEW badge overflowed sidebar");
                var dialog = new UpdateWindow(main, NewRelease);
                if ((string)((Button)dialog.FindName("ConfirmButton")).Content != Loc.T("ui.update.confirm")) throw new Exception("Incorrect update button label");
                dialog.Close();
                main.ApplyUpdateResult(new(UpdateOutcome.Current)); page.RefreshUpdateState();
                if (badge.Visibility != Visibility.Collapsed || ((FrameworkElement)page.FindName("UpdateAvailableBadge")).Visibility != Visibility.Collapsed) throw new Exception("Stale update badge");
                foreach (var (outcome, key) in new[] { (UpdateOutcome.NoRelease, "ui.update.no.release"), (UpdateOutcome.IncompleteRelease, "ui.update.incomplete"), (UpdateOutcome.Failed, "update.failed") })
                {
                    main.ApplyUpdateResult(new(outcome)); page.RefreshUpdateState();
                    if (((TextBlock)page.FindName("UpdateStateText")).Text != Loc.T(key)) throw new Exception("Update failure explanation incorrect");
                }
            }
            finally { main.ForceClose(); }
        }
        Loc.SetLanguage("zh-CN");
        var owner = new MainWindow();
        var window = new UpdateWindow(owner, NewRelease, (_, _, _) => Task.FromResult(new DownloadedUpdate("test-only", "", 1)),
            (_, _) => throw new IOException("Simulated launch failure"));
        AppSelfTest.RunUiTask(window.ConfirmAsync());
        if (!owner.CanInteract || !((Button)window.FindName("ConfirmButton")).IsEnabled) throw new Exception("Launch failure closed app or blocked retry");
        window.Close(); owner.ForceClose();
        owner = new MainWindow(); bool installed = false;
        window = new UpdateWindow(owner, NewRelease, (_, _, _) => Task.FromResult(new DownloadedUpdate("test-only", "", 1)),
            (_, _) => { installed = true; return Task.CompletedTask; });
        AppSelfTest.RunUiTask(window.ConfirmAsync());
        if (!installed || !owner.IsClosed) throw new Exception("Confirmed update did not finish handoff");
        owner = new MainWindow(); installed = false;
        var pending = new TaskCompletionSource<DownloadedUpdate>(TaskCreationOptions.RunContinuationsAsynchronously);
        window = new UpdateWindow(owner, NewRelease, (_, _, token) => pending.Task.WaitAsync(token),
            (_, _) => { installed = true; return Task.CompletedTask; });
        var downloading = window.ConfirmAsync();
        window.Close();
        AppSelfTest.RunUiTask(downloading);
        if (installed || !owner.CanInteract) throw new Exception("Cancelled update started installer or closed app");
        owner.ForceClose();
    }
}
