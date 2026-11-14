using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace CleanOptimize.App.Services;

internal enum UpdateOutcome { NotChecked, Current, Available, NoRelease, Failed }
internal sealed record UpdateResult(UpdateOutcome Outcome, string? Version = null, string? Tag = null, long InstallerBytes = 0)
{
    internal bool HasUpdate => Version != null && Tag != null;
}
internal sealed record ReleaseResponse(int StatusCode, string Body);

/// <summary>Only public release metadata is requested; no telemetry, identifiers or file data.</summary>
public sealed partial class UpdateService
{
    internal const string Endpoint = "https://api.github.com/repos/HZ-PRE/cleanwin/releases/latest";
    internal const int MaxResponseBytes = 256 * 1024;
    private static readonly Lazy<HttpClient> Client = new(() => new HttpClient(new HttpClientHandler
    {
        AllowAutoRedirect = false, UseCookies = false, UseDefaultCredentials = false,
    }) { Timeout = TimeSpan.FromSeconds(8) });
    private readonly object _gate = new();
    private readonly Func<CancellationToken, Task<ReleaseResponse>> _fetch;
    private readonly Func<DateTimeOffset> _now;
    private readonly string _currentVersion;
    private Task<UpdateResult>? _pending;
    private DateTimeOffset _nextCheck;
    private UpdateResult _last = new(UpdateOutcome.NotChecked);

    public static string CurrentVersion => Assembly.GetEntryAssembly()?
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "1.0.0";
    public static string PrivacyMessage => Loc.T("privacy.offline");
    public UpdateService() : this(FetchAsync, CurrentVersion, () => DateTimeOffset.UtcNow) { }
    internal UpdateService(Func<CancellationToken, Task<ReleaseResponse>> fetch, string currentVersion, Func<DateTimeOffset> now)
    { _fetch = fetch; _currentVersion = currentVersion; _now = now; }

    internal Task<UpdateResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_pending is { IsCompleted: false }) return _pending;
            if (_now() < _nextCheck) return Task.FromResult(_last);
            _pending = Task.Run(() => CheckCoreAsync(cancellationToken), cancellationToken);
            return _pending;
        }
    }

    private async Task<UpdateResult> CheckCoreAsync(CancellationToken cancellationToken)
    {
        UpdateResult result;
        try
        {
            var response = await _fetch(cancellationToken).ConfigureAwait(false);
            result = response.StatusCode switch
            {
                200 => ParseRelease(response.Body, _currentVersion),
                404 => new(UpdateOutcome.NoRelease),
                _ => new(UpdateOutcome.Failed),
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException or JsonException or InvalidDataException)
        { result = new(UpdateOutcome.Failed); }
        lock (_gate)
        {
            // A temporary network failure must not erase an already verified update.
            if (result.Outcome == UpdateOutcome.Failed && _last.HasUpdate) result = _last with { Outcome = UpdateOutcome.Failed };
            _last = result;
            _nextCheck = _now().AddMinutes(5);
            return result;
        }
    }

    internal static HttpRequestMessage CreateRequest()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, Endpoint);
        request.Headers.UserAgent.ParseAdd("CleanOptimize-UpdateCheck");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        return request;
    }

    private static async Task<ReleaseResponse> FetchAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        using var request = CreateRequest();
        using var response = await Client.Value.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.OK) return new((int)response.StatusCode, "");
        if (response.Content.Headers.ContentLength is > MaxResponseBytes) throw new InvalidDataException("Release response too large");
        using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
        return new(200, await ReadBodyAsync(stream, timeout.Token).ConfigureAwait(false));
    }

    internal static async Task<string> ReadBodyAsync(Stream stream, CancellationToken token)
    {
        using var buffer = new MemoryStream();
        var bytes = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(bytes.AsMemory(), token).ConfigureAwait(false)) != 0)
        {
            if (buffer.Length + count > MaxResponseBytes) throw new InvalidDataException("Release response too large");
            buffer.Write(bytes, 0, count);
        }
        return System.Text.Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    internal static UpdateResult ParseRelease(string json, string current)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("draft", out var draft) || draft.ValueKind != JsonValueKind.False ||
            !root.TryGetProperty("prerelease", out var preview) || preview.ValueKind != JsonValueKind.False ||
            !root.TryGetProperty("tag_name", out var tagValue) || tagValue.ValueKind != JsonValueKind.String)
            return new(UpdateOutcome.NoRelease);
        var tag = tagValue.GetString()!;
        var number = tag.StartsWith('v') ? tag[1..] : tag;
        if (!TryStableVersion(number, out var latest)) return new(UpdateOutcome.NoRelease);
        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return new(UpdateOutcome.NoRelease);
        long installerBytes = 0;
        bool hasChecksum = false;
        foreach (var a in assets.EnumerateArray())
        {
            if (a.ValueKind == JsonValueKind.Object &&
                a.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String && n.GetString() == $"CleanOptimizeSetup-{number}.exe" &&
                a.TryGetProperty("state", out var s) && s.ValueKind == JsonValueKind.String && s.GetString() == "uploaded" &&
                a.TryGetProperty("size", out var size) && size.ValueKind == JsonValueKind.Number && size.TryGetInt64(out long length) && length > 0 && length <= MaxInstallerBytes)
                installerBytes = length;
            if (a.ValueKind == JsonValueKind.Object && a.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String && name.GetString() == "SHA256SUMS.txt" &&
                a.TryGetProperty("state", out var state) && state.ValueKind == JsonValueKind.String && state.GetString() == "uploaded") hasChecksum = true;
        }
        if (installerBytes == 0 || !hasChecksum) return new(UpdateOutcome.NoRelease);
        var installed = current.Split('+')[0];
        bool installedPreview = installed.Contains('-');
        if (!TryStableVersion(installed.Split('-')[0], out var local)) return new(UpdateOutcome.Failed);
        return latest > local || (latest == local && installedPreview)
            ? new(UpdateOutcome.Available, number, tag, installerBytes) : new(UpdateOutcome.Current);
    }

    private static bool TryStableVersion(string text, out Version version)
    {
        version = new Version(0, 0, 0);
        if (text.Length > 40) return false;
        var parts = text.Split('.');
        return parts.Length == 3 && parts.All(p => p.Length > 0 && p.All(c => c is >= '0' and <= '9')) &&
            Version.TryParse(text, out version!);
    }

}
