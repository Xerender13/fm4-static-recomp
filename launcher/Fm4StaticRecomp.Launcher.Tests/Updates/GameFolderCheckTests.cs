using Fm4StaticRecomp.Launcher.Core.Discs;
using Fm4StaticRecomp.Launcher.Core.Updates;
using Fm4StaticRecomp.Launcher.Tests.Discs;

namespace Fm4StaticRecomp.Launcher.Tests.Updates;

public class GameFolderCheckTests
{
    private static DiscManifest Manifest(TempDir temp, Dictionary<string, byte[]> files) =>
        DiscManifest.Load(TestDiscs.WriteManifest(temp, files));

    [Fact]
    public void The_same_manifest_needs_no_extraction()
    {
        using var temp = new TempDir();
        var installed = Manifest(temp, TestDiscs.Pal());
        Assert.False(GameFolderCheck.NeedsExtraction(installed, Manifest(temp, TestDiscs.Pal()), TestDiscs.PalName));
    }

    [Fact]
    public void A_new_required_file_needs_extraction()
    {
        using var temp = new TempDir();
        var installed = Manifest(temp, TestDiscs.Pal());
        var files = TestDiscs.Pal();
        files["Media/c.bin"] = [3];
        Assert.True(GameFolderCheck.NeedsExtraction(installed, Manifest(temp, files), TestDiscs.PalName));
    }

    [Fact]
    public void A_new_optional_file_needs_none()
    {
        using var temp = new TempDir();
        var installed = Manifest(temp, TestDiscs.Pal());
        var files = TestDiscs.Pal();
        files["$SystemUpdate/x"] = [3];
        Assert.False(GameFolderCheck.NeedsExtraction(installed, Manifest(temp, files), TestDiscs.PalName));
    }

    [Fact]
    public void A_release_the_new_manifest_drops_needs_extraction()
    {
        using var temp = new TempDir();
        var installed = Manifest(temp, TestDiscs.Pal());
        var next = installed with { Disc1 = installed.Disc1 with { Releases = [installed.Disc1.Releases[1]] } };
        Assert.True(GameFolderCheck.NeedsExtraction(installed, next, TestDiscs.PalName));
    }

    [Fact]
    public void Checks_the_installed_executable()
    {
        using var temp = new TempDir();
        var manifest = Manifest(temp, TestDiscs.Pal());
        var xex = TestDiscs.Pal()["default.xex"];
        var game = Path.Combine(temp.Path, "game");
        temp.File("game/default.xex", xex);
        Assert.True(GameFolderCheck.ExecutableMatches(game, manifest, TestDiscs.PalName));
        xex[^1] ^= 1;
        File.WriteAllBytes(Path.Combine(game, "default.xex"), xex);
        Assert.False(GameFolderCheck.ExecutableMatches(game, manifest, TestDiscs.PalName));
        Assert.False(GameFolderCheck.ExecutableMatches(Path.Combine(temp.Path, "none"), manifest, TestDiscs.PalName));
    }
}
