using System.Net.Http.Headers;
using System.Text.Json;

namespace Fm4StaticRecomp.Launcher.Core.Updates;

// Switches the maintainer flips for every installed launcher with one commit to the
// repository's main branch (spec 11). The last value read is cached; with no network the
// cached value holds, and with no cache the default.
public sealed record LauncherFlags(bool CrashReports)
{
    public static readonly LauncherFlags Default = new(true);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    public static Uri Url(string ownerRepo) =>
        new($"https://raw.githubusercontent.com/{ownerRepo}/main/config/launcher-flags.json");

    public static LauncherFlags LoadCached(string path)
    {
        try { return File.Exists(path) ? Parse(File.ReadAllText(path)) ?? Default : Default; }
        catch (IOException) { return Default; }
        catch (UnauthorizedAccessException) { return Default; }
    }

    public static async Task<LauncherFlags> RefreshAsync(HttpClient http, Uri url, string cachePath, CancellationToken cancellation)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(Timeout);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue(ReleaseClient.UserAgent, "1"));
            using var response = await http.SendAsync(request, timeout.Token);
            if (!response.IsSuccessStatusCode) return LoadCached(cachePath);
            if (Parse(await response.Content.ReadAsStringAsync(timeout.Token)) is not { } flags) return LoadCached(cachePath);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(cachePath))!);
            File.WriteAllText(cachePath, JsonSerializer.Serialize(new Dictionary<string, bool> { ["crash_reports"] = flags.CrashReports }));
            return flags;
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested) { return LoadCached(cachePath); }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException) { return LoadCached(cachePath); }
    }

    private static LauncherFlags? Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object &&
                   document.RootElement.TryGetProperty("crash_reports", out var value) &&
                   value.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? new LauncherFlags(value.GetBoolean())
                : null;
        }
        catch (JsonException) { return null; }
    }
}
