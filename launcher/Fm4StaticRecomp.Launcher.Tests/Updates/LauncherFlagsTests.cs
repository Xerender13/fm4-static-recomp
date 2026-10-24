using System.Net;
using Fm4StaticRecomp.Launcher.Core.Updates;

namespace Fm4StaticRecomp.Launcher.Tests.Updates;

public class LauncherFlagsTests
{
    private static readonly Uri Url = LauncherFlags.Url("o/r");

    private static Task<LauncherFlags> Refresh(Func<HttpRequestMessage, HttpResponseMessage> answer, string cache)
    {
        var handler = new FakeHandler();
        handler.Routes[Url.AbsoluteUri] = answer;
        return LauncherFlags.RefreshAsync(new HttpClient(handler), Url, cache, CancellationToken.None);
    }

    [Fact]
    public void Reads_the_flags_from_the_repository_s_main_branch() =>
        Assert.Equal(new Uri("https://raw.githubusercontent.com/i2kiselev/fm4-static-recomp/main/config/launcher-flags.json"),
            LauncherFlags.Url("i2kiselev/fm4-static-recomp"));

    [Fact]
    public async Task Keeps_the_last_answer_when_the_server_fails()
    {
        using var temp = new TempDir();
        var cache = Path.Combine(temp.Path, "config", "launcher-flags.json");
        Assert.False((await Refresh(_ => FakeHandler.Json("""{"crash_reports": false}"""), cache)).CrashReports);
        Assert.False(LauncherFlags.LoadCached(cache).CrashReports);
        Assert.False((await Refresh(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError), cache)).CrashReports);
        Assert.False((await Refresh(_ => FakeHandler.Json("""{"crash_reports": "no"}"""), cache)).CrashReports);
        Assert.False((await Refresh(_ => FakeHandler.Json("not json"), cache)).CrashReports);
        Assert.True((await Refresh(_ => FakeHandler.Json("""{"crash_reports": true, "later": 1}"""), cache)).CrashReports);
        Assert.True(LauncherFlags.LoadCached(cache).CrashReports);
    }

    [Fact]
    public async Task Defaults_to_on_without_a_cache()
    {
        using var temp = new TempDir();
        var cache = Path.Combine(temp.Path, "launcher-flags.json");
        Assert.Equal(LauncherFlags.Default, await Refresh(_ => new HttpResponseMessage(HttpStatusCode.NotFound), cache));
        Assert.True(LauncherFlags.Default.CrashReports);
        File.WriteAllText(cache, "garbage");
        Assert.Equal(LauncherFlags.Default, LauncherFlags.LoadCached(cache));
    }

    [Fact]
    public async Task A_cancelled_start_throws()
    {
        using var temp = new TempDir();
        var cache = Path.Combine(temp.Path, "launcher-flags.json");
        var hanging = new HangingHandler();
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            LauncherFlags.RefreshAsync(new HttpClient(hanging), Url, cache, cancel.Token));
    }

    private sealed class HangingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException();
        }
    }
}
