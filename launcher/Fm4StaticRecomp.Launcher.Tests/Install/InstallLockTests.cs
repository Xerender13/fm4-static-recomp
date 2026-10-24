using Fm4StaticRecomp.Launcher.Core.Install;

namespace Fm4StaticRecomp.Launcher.Tests.Install;

public class InstallLockTests
{
    [Fact]
    public void One_launcher_at_a_time_holds_an_install()
    {
        using var temp = new TempDir();
        var first = InstallLock.TryAcquire(temp.Path, TimeSpan.Zero);
        Assert.NotNull(first);
        Assert.Null(InstallLock.TryAcquire(temp.Path, TimeSpan.Zero));
        first!.Dispose();
        using var third = InstallLock.TryAcquire(temp.Path, TimeSpan.Zero);
        Assert.NotNull(third);
    }

    [Fact]
    public async Task Waits_for_the_launcher_that_is_closing()
    {
        // An updated launcher starts while the old one is still shutting down.
        using var temp = new TempDir();
        var old = InstallLock.TryAcquire(temp.Path, TimeSpan.Zero)!;
        var release = Task.Delay(300).ContinueWith(_ => old.Dispose());
        using var next = InstallLock.TryAcquire(temp.Path, TimeSpan.FromSeconds(10));
        Assert.NotNull(next);
        await release;
    }
}
