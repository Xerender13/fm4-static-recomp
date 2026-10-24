using Fm4StaticRecomp.Launcher.Core.Updates;

namespace Fm4StaticRecomp.Launcher.Tests.Updates;

public class SelfReplaceTests
{
    [Fact]
    public void Swaps_the_executable_and_cleans_up_the_old_one()
    {
        using var temp = new TempDir();
        var running = temp.File("FM4StaticRecompLauncher.exe", "old"u8.ToArray());
        var fresh = temp.File("downloads/FM4StaticRecompLauncher.exe", "new"u8.ToArray());
        SelfReplace.Replace(running, fresh);
        Assert.Equal("new", File.ReadAllText(running));
        Assert.Equal("old", File.ReadAllText(running + ".old"));
        SelfReplace.CleanUp(running);
        Assert.False(File.Exists(running + ".old"));
    }

    [Fact]
    public void Puts_the_old_one_back_when_the_new_one_cannot_be_moved_in()
    {
        using var temp = new TempDir();
        var running = temp.File("FM4StaticRecompLauncher.exe", "old"u8.ToArray());
        Assert.ThrowsAny<IOException>(() => SelfReplace.Replace(running, Path.Combine(temp.Path, "missing.exe")));
        Assert.Equal("old", File.ReadAllText(running));
        Assert.False(File.Exists(running + ".old"));
    }
}
