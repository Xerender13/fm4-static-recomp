using System.Diagnostics;
using Fm4StaticRecomp.Launcher.Core.Content;
using Fm4StaticRecomp.Launcher.Core.Setup;

namespace Fm4StaticRecomp.Launcher.Tests.Content;

public class ContentInstallerTests
{
    private static Process Sleeper(string seconds)
    {
        var start = new ProcessStartInfo(PowerShell.Executable()) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in new[] { "-NoLogo", "-NoProfile", "-Command", $"Start-Sleep -Seconds {seconds}" })
            start.ArgumentList.Add(argument);
        return Process.Start(start)!;
    }

    [Fact]
    public async Task Cancelling_stops_the_game()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var temp = new TempDir();
        using var process = Sleeper("60");
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        var clock = Stopwatch.StartNew();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ContentInstaller.FollowAsync(process,
            Path.Combine(temp.Path, "content-install.log"), new SyncProgress<ContentEvent>(_ => { }), cancel.Token));
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), clock.Elapsed.ToString());
        Assert.True(process.HasExited);
    }

    [Fact]
    public async Task Reports_the_log_s_lines_until_the_game_exits()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var temp = new TempDir();
        var log = temp.File("content-install.log",
            "[info] Content: installed 4d53091000000001\n[info] Content: install finished, 1 installed, 0 skipped, 0 failed\n"u8.ToArray());
        using var process = Sleeper("0");
        var events = new List<ContentEvent>();
        var finished = await ContentInstaller.FollowAsync(process, log, new SyncProgress<ContentEvent>(events.Add), CancellationToken.None);
        Assert.Equal(new ContentEvent.Finished(1, 0, 0), finished);
        Assert.Equal(2, events.Count);
    }
}
