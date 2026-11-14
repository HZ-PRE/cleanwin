using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using CleanOptimize.Core.Store;
using CleanOptimize.Core.Utils;

namespace CleanOptimize.App.Services;

internal sealed record DownloadedUpdate(string Path, string Sha256, long Bytes);

public sealed partial class UpdateService
{
    private const string DownloadBase = "https://github.com/HZ-PRE/cleanwin/releases/download/";
    internal const long MaxInstallerBytes = 512L * 1024 * 1024;

    internal static Uri AssetUri(UpdateResult result, bool checksum)
    {
        if (!result.HasUpdate || !TryStableVersion(result.Version!, out _) ||
            (result.Tag != result.Version && result.Tag != "v" + result.Version)) throw new InvalidDataException("Invalid release tag");
        return new Uri(DownloadBase + Uri.EscapeDataString(result.Tag!) + "/" +
            (checksum ? "SHA256SUMS.txt" : $"CleanOptimizeSetup-{result.Version}.exe"));
    }

    internal static bool AllowedDownloadUri(Uri uri) => uri.IsAbsoluteUri && uri.Scheme == Uri.UriSchemeHttps &&
        uri.IsDefaultPort && string.IsNullOrEmpty(uri.UserInfo) &&
        uri.Host is "github.com" or "release-assets.githubusercontent.com" or "objects.githubusercontent.com";

    private static async Task<HttpResponseMessage> GetDownloadAsync(Uri uri, CancellationToken token)
    {
        for (int hop = 0; hop < 5; hop++)
        {
            if (!AllowedDownloadUri(uri)) throw new InvalidDataException("Untrusted download redirect");
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.UserAgent.ParseAdd("CleanOptimize-UpdateCheck");
            var response = await Client.Value.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
            {
                var redirect = response.Headers.Location;
                response.Dispose();
                if (redirect == null) throw new InvalidDataException("Missing redirect location");
                uri = redirect.IsAbsoluteUri ? redirect : new Uri(uri, redirect);
                continue;
            }
            if (!response.IsSuccessStatusCode) { response.Dispose(); throw new HttpRequestException("Update asset unavailable"); }
            return response;
        }
        throw new InvalidDataException("Too many download redirects");
    }

    internal static string ParseChecksum(string manifest, string filename)
    {
        string? hash = null;
        foreach (var raw in manifest.TrimStart('\uFEFF').Split('\n'))
        {
            var fields = raw.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length != 2 || fields[1].TrimStart('*') != filename) continue;
            if (hash != null || fields[0].Length != 64 || !fields[0].All(Uri.IsHexDigit)) throw new InvalidDataException("Invalid checksum entry");
            hash = fields[0].ToUpperInvariant();
        }
        return hash ?? throw new InvalidDataException("Installer checksum missing");
    }

    internal static async Task<DownloadedUpdate> DownloadAsync(UpdateResult release, IProgress<long>? progress, CancellationToken cancellationToken)
    {
        if (release.InstallerBytes is <= 0 or > MaxInstallerBytes) throw new InvalidDataException("Invalid installer size");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        using var checksumResponse = await GetDownloadAsync(AssetUri(release, true), timeout.Token).ConfigureAwait(false);
        if (checksumResponse.Content.Headers.ContentLength is > MaxResponseBytes) throw new InvalidDataException("Checksum response too large");
        using var checksumStream = await checksumResponse.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        var hash = ParseChecksum(await ReadBodyAsync(checksumStream, timeout.Token).ConfigureAwait(false), $"CleanOptimizeSetup-{release.Version}.exe");
        var directory = System.IO.Path.Combine(SettingsService.DataDir, "Updates");
        if (!PathUtil.IsLocalPath(directory) || PathUtil.HasReparseAncestor(directory)) throw new IOException("Invalid update directory");
        Directory.CreateDirectory(directory);
        if (PathUtil.HasReparseAncestor(directory)) throw new IOException("Invalid update directory");
        var final = System.IO.Path.Combine(directory, $"CleanOptimizeSetup-{release.Version}-{Guid.NewGuid():N}.exe");
        using var response = await GetDownloadAsync(AssetUri(release, false), timeout.Token).ConfigureAwait(false);
        if (response.Content.Headers.ContentLength is long size && size != release.InstallerBytes) throw new InvalidDataException("Installer length mismatch");
        using var source = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        return await SaveVerifiedAsync(source, final, release.InstallerBytes, hash, progress, timeout.Token).ConfigureAwait(false);
    }

    internal static async Task<DownloadedUpdate> SaveVerifiedAsync(Stream source, string final, long bytes, string hash, IProgress<long>? progress, CancellationToken token)
    {
        if (!PathUtil.IsLocalPath(final) || PathUtil.HasReparseAncestor(final)) throw new IOException("Invalid update file");
        var partial = final + ".part";
        bool created = false;
        try
        {
            await using (var destination = new FileStream(partial, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous))
            {
                created = true;
                await CopyVerifiedAsync(source, destination, bytes, hash, progress, token).ConfigureAwait(false);
            }
            token.ThrowIfCancellationRequested();
            if (PathUtil.HasReparseAncestor(partial)) throw new IOException("Invalid update file");
            File.Move(partial, final);
            return new DownloadedUpdate(final, hash, bytes);
        }
        finally { if (created && !PathUtil.HasReparseAncestor(partial) && File.Exists(partial)) File.Delete(partial); }
    }

    internal static async Task CopyVerifiedAsync(Stream source, Stream destination, long expectedBytes, string expectedHash, IProgress<long>? progress, CancellationToken token)
    {
        if (expectedBytes is <= 0 or > MaxInstallerBytes) throw new InvalidDataException("Invalid installer size");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[65536];
        var clock = System.Diagnostics.Stopwatch.StartNew();
        long total = 0, lastReport = 0;
        int read;
        while ((read = await source.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false)) > 0)
        {
            total += read;
            if (total > expectedBytes) throw new InvalidDataException("Installer too large");
            hash.AppendData(buffer, 0, read);
            await destination.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
            if (clock.ElapsedMilliseconds - lastReport >= 150) { progress?.Report(total); lastReport = clock.ElapsedMilliseconds; }
        }
        if (total != expectedBytes || !Convert.ToHexString(hash.GetHashAndReset()).Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Installer integrity check failed");
        await destination.FlushAsync(token).ConfigureAwait(false);
        progress?.Report(total);
    }

    internal static async Task StartInstallerAsync(DownloadedUpdate downloaded, CancellationToken token)
    {
        var directory = System.IO.Path.Combine(SettingsService.DataDir, "Updates");
        if (!PathUtil.IsUnder(downloaded.Path, directory) || PathUtil.HasReparseAncestor(downloaded.Path)) throw new IOException("Invalid installer path");
        // Hold a read-only sharing handle through launch so verified bytes cannot be replaced.
        using var locked = new FileStream(downloaded.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous);
        if (locked.Length != downloaded.Bytes || locked.ReadByte() != 'M' || locked.ReadByte() != 'Z') throw new InvalidDataException("Invalid installer executable");
        locked.Position = 0;
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(locked, token));
        if (!hash.Equals(downloaded.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Installer changed before launch");
        token.ThrowIfCancellationRequested();
        var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(downloaded.Path) { UseShellExecute = true });
        if (process == null) throw new IOException("Installer could not start");
        process.Dispose();
    }
}
