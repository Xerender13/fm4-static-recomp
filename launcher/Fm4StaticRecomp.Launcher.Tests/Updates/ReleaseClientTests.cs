using System.Net;
using Fm4StaticRecomp.Launcher.Core.Updates;

namespace Fm4StaticRecomp.Launcher.Tests.Updates;

public class ReleaseClientTests
{
    private const string Api = "https://api.test";
    private const string List = "https://api.test/repos/o/r/releases?per_page=30";

    private static string Release(string tag, bool prerelease = false, bool draft = false, string notes = "") => $$"""
        {"tag_name":"{{tag}}","body":"{{notes}}","prerelease":{{(prerelease ? "true" : "false")}},"draft":{{(draft ? "true" : "false")}},
         "assets":[{"name":"FM4StaticRecompLauncher.exe","browser_download_url":"https://example.test/{{tag}}/l.exe"},
                   {"name":"fm4-static-recomp-source.zip","browser_download_url":"https://example.test/{{tag}}/s.zip"},
                   {"name":"SHA256SUMS.txt","browser_download_url":"https://example.test/{{tag}}/sums"}]}
        """;

    [Fact]
    public async Task Takes_the_highest_release_prereleases_included()
    {
        var handler = new FakeHandler();
        handler.Routes[List] = _ => FakeHandler.Json("[" + string.Join(",",
            Release("v0.1.3"), Release("v0.2.0", prerelease: true, notes: "Faster races.\\nMore."), Release("v0.3.0", draft: true),
            Release("v0.2.1-preview.1", prerelease: true), Release("nightly", prerelease: true)) + "]");
        var release = await new ReleaseClient(new HttpClient(handler), "o/r", Api).NewestAsync(CancellationToken.None);
        Assert.Equal(new Version(0, 2, 0), release!.Version);
        Assert.Equal("v0.2.0", release.Tag);
        Assert.True(release.Prerelease);
        Assert.Equal("Faster races.\nMore.", release.Notes);
        Assert.Equal(new Uri("https://example.test/v0.2.0/l.exe"), release.Assets[ReleaseClient.LauncherAsset]);
        var request = handler.Requests.Single();
        Assert.Contains(request.Headers.UserAgent, p => p.Product?.Name == "FM4StaticRecompLauncher");
        Assert.EndsWith("/releases?per_page=30", request.RequestUri!.AbsoluteUri);
    }

    [Fact]
    public async Task Skips_a_release_without_the_launcher_s_assets()
    {
        // The project's source-only releases (v0.1.3) came before the launcher.
        var handler = new FakeHandler();
        handler.Routes[List] = _ => FakeHandler.Json("[" + string.Join(",",
            Release("v0.1.1"),
            """{"tag_name":"v0.1.3","body":"","prerelease":false,"draft":false,"assets":[]}""") + "]");
        var release = await new ReleaseClient(new HttpClient(handler), "o/r", Api).NewestAsync(CancellationToken.None);
        Assert.Equal(new Version(0, 1, 1), release!.Version);
    }

    [Fact]
    public async Task An_empty_list_is_null()
    {
        var handler = new FakeHandler();
        handler.Routes[List] = _ => FakeHandler.Json("[]");
        Assert.Null(await new ReleaseClient(new HttpClient(handler), "o/r", Api).NewestAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Not_found_is_null() =>
        Assert.Null(await new ReleaseClient(new HttpClient(new FakeHandler()), "o/r", Api).NewestAsync(CancellationToken.None));

    [Fact]
    public async Task A_server_error_throws()
    {
        var handler = new FakeHandler();
        handler.Routes[List] = _ => new HttpResponseMessage(HttpStatusCode.Forbidden);
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            new ReleaseClient(new HttpClient(handler), "o/r", Api).NewestAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData("<html>Sign in to the hotel wifi</html>")]
    [InlineData("""{"message":"moved"}""")]
    [InlineData("""[{"tag_name":"v1.0.0","assets":[{"name":"x"}]}]""")]
    public async Task An_unexpected_answer_is_a_request_error(string body)
    {
        var handler = new FakeHandler();
        handler.Routes[List] = _ => FakeHandler.Json(body);
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            new ReleaseClient(new HttpClient(handler), "o/r", Api).NewestAsync(CancellationToken.None));
    }

    [Fact]
    public async Task A_timeout_is_a_request_error_and_a_cancel_is_not()
    {
        var http = new HttpClient(new HangingHandler()) { Timeout = TimeSpan.FromMilliseconds(200) };
        await Assert.ThrowsAsync<HttpRequestException>(() => new ReleaseClient(http, "o/r", Api).NewestAsync(CancellationToken.None));
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new ReleaseClient(new HttpClient(new HangingHandler()), "o/r", Api).NewestAsync(cancel.Token));
    }

    [Fact]
    public async Task Unreadable_checksums_are_a_request_error()
    {
        var http = new HttpClient(new HangingHandler()) { Timeout = TimeSpan.FromMilliseconds(200) };
        var release = new ReleaseInfo(new Version(1, 0, 0), "v1.0.0", false, "",
            new Dictionary<string, Uri> { [ReleaseClient.SumsAsset] = new("https://example.test/sums") });
        await Assert.ThrowsAsync<HttpRequestException>(() => new ReleaseClient(http, "o/r", Api).SumsAsync(release, CancellationToken.None));
    }

    private sealed class HangingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException();
        }
    }

    [Theory]
    [InlineData("v1.2.3", "1.2.3")]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("v1.2", null)]
    [InlineData("v1.2.3-beta", null)]
    [InlineData("v1.2.3.4", null)]
    [InlineData("nightly", null)]
    public void Parses_tags(string tag, string? expected) =>
        Assert.Equal(expected is null ? null : Version.Parse(expected), ReleaseClient.ParseVersion(tag));

    [Theory]
    [InlineData("https://github.com/i2kiselev/fm4-static-recomp", "i2kiselev/fm4-static-recomp")]
    [InlineData("https://github.com/i2kiselev/fm4-static-recomp.git", "i2kiselev/fm4-static-recomp")]
    [InlineData("https://github.com/i2kiselev/fm4-static-recomp/", "i2kiselev/fm4-static-recomp")]
    public void Reads_the_repository(string url, string ownerRepo) => Assert.Equal(ownerRepo, ReleaseClient.OwnerRepo(url));

    [Theory]
    [InlineData("https://example.com/a/b")]
    [InlineData("https://github.com/only-owner")]
    public void Refuses_a_repository_off_github(string url) =>
        Assert.Throws<InvalidDataException>(() => ReleaseClient.OwnerRepo(url));

    [Fact]
    public async Task Reads_checksums()
    {
        var handler = new FakeHandler();
        handler.Routes["https://example.test/sums"] = _ => FakeHandler.Json("ABCD  FM4StaticRecompLauncher.exe\nef01 *fm4-static-recomp-source.zip\n");
        var release = new ReleaseInfo(new Version(1, 0, 0), "v1.0.0", false, "",
            new Dictionary<string, Uri> { [ReleaseClient.SumsAsset] = new("https://example.test/sums") });
        var sums = await new ReleaseClient(new HttpClient(handler), "o/r", Api).SumsAsync(release, CancellationToken.None);
        Assert.Equal("ABCD", sums[ReleaseClient.LauncherAsset]);
        Assert.Equal("EF01", sums[ReleaseClient.SourceAsset]);
    }
}
