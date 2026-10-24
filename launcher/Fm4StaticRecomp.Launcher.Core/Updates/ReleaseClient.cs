using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Fm4StaticRecomp.Launcher.Core.Updates;

// apiBase is GitHub's API; the end-to-end tests point it at a local fake release server.
public sealed partial class ReleaseClient(HttpClient http, string ownerRepo, string apiBase)
{
    public const string LauncherAsset = "FM4StaticRecompLauncher.exe";
    public const string SourceAsset = "fm4-static-recomp-source.zip";
    public const string SumsAsset = "SHA256SUMS.txt";
    public const string UserAgent = "FM4StaticRecompLauncher";
    public const string ApiVariable = "FM4_STATIC_RECOMP_UPDATE_API";
    private const string GitHubApi = "https://api.github.com";

    public static string ApiBase() =>
        Environment.GetEnvironmentVariable(ApiVariable) is { Length: > 0 } api ? api : GitHubApi;

    // Every failure but the caller's own cancel is an HttpRequestException, which the launcher
    // shows as a quiet note (spec 13): a captive portal's page, a changed answer, a timeout.
    public async Task<ReleaseInfo?> NewestAsync(CancellationToken cancellation)
    {
        try { return await ReadNewestAsync(cancellation); }
        catch (Exception ex) when (IsUnexpected(ex, cancellation))
        {
            throw new HttpRequestException($"The releases could not be read: {ex.Message}", ex);
        }
    }

    public async Task<IReadOnlyDictionary<string, string>> SumsAsync(ReleaseInfo release, CancellationToken cancellation)
    {
        try { return await ReadSumsAsync(release, cancellation); }
        catch (Exception ex) when (IsUnexpected(ex, cancellation))
        {
            throw new HttpRequestException($"The checksums could not be read: {ex.Message}", ex);
        }
    }

    // The list, not /releases/latest: the project publishes prereleases, which "latest" skips.
    private async Task<ReleaseInfo?> ReadNewestAsync(CancellationToken cancellation)
    {
        using var request = Get(new Uri($"{apiBase.TrimEnd('/')}/repos/{ownerRepo}/releases?per_page=30"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var response = await http.SendAsync(request, cancellation);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync(cancellation));
        ReleaseInfo? newest = null;
        foreach (var release in document.RootElement.EnumerateArray())
        {
            if (Flag(release, "draft")) continue;
            var tag = release.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
            if (ParseVersion(tag) is not { } version || newest is not null && version <= newest.Version) continue;
            var assets = release.TryGetProperty("assets", out var list)
                ? list.EnumerateArray().ToDictionary(
                    a => a.GetProperty("name").GetString()!,
                    a => new Uri(a.GetProperty("browser_download_url").GetString()!))
                : [];
            // A release the launcher cannot install from (the source-only releases before it).
            if (!assets.ContainsKey(LauncherAsset) || !assets.ContainsKey(SourceAsset) || !assets.ContainsKey(SumsAsset)) continue;
            var notes = release.TryGetProperty("body", out var body) && body.ValueKind == JsonValueKind.String
                ? body.GetString()!
                : "";
            newest = new ReleaseInfo(version, tag, Flag(release, "prerelease"), notes, assets);
        }
        return newest;
    }

    private async Task<IReadOnlyDictionary<string, string>> ReadSumsAsync(ReleaseInfo release, CancellationToken cancellation)
    {
        using var request = Get(release.Assets[SumsAsset]);
        using var response = await http.SendAsync(request, cancellation);
        response.EnsureSuccessStatusCode();
        var text = await response.Content.ReadAsStringAsync(cancellation);
        return text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(l => l.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries))
            .Where(p => p.Length == 2)
            .ToDictionary(p => p[1].TrimStart('*').Trim(), p => p[0].ToUpperInvariant());
    }

    public static Version? ParseVersion(string tag)
    {
        var match = Tag().Match(tag);
        return match.Success &&
               int.TryParse(match.Groups[1].Value, out var major) &&
               int.TryParse(match.Groups[2].Value, out var minor) &&
               int.TryParse(match.Groups[3].Value, out var patch)
            ? new Version(major, minor, patch)
            : null;
    }

    public static string OwnerRepo(string repositoryUrl)
    {
        if (!Uri.TryCreate(repositoryUrl, UriKind.Absolute, out var uri) ||
            !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"{repositoryUrl} is not a GitHub repository.");
        var path = uri.AbsolutePath.Trim('/');
        if (path.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) path = path[..^4];
        var parts = path.Split('/');
        if (parts.Length != 2 || parts.Any(p => p.Length == 0))
            throw new InvalidDataException($"{repositoryUrl} is not a GitHub repository.");
        return $"{parts[0]}/{parts[1]}";
    }

    private static HttpRequestMessage Get(Uri url)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue(UserAgent, "1"));
        return request;
    }

    private static bool IsUnexpected(Exception ex, CancellationToken cancellation) =>
        ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or ArgumentException ||
        ex is OperationCanceledException && !cancellation.IsCancellationRequested;

    private static bool Flag(JsonElement release, string name) =>
        release.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    [GeneratedRegex(@"^v?(\d+)\.(\d+)\.(\d+)$")]
    private static partial Regex Tag();
}
