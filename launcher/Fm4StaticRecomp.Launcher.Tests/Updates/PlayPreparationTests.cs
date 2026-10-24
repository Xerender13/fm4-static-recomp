using Fm4StaticRecomp.Launcher.Core.Install;
using Fm4StaticRecomp.Launcher.Core.Updates;
using Fm4StaticRecomp.Launcher.Core.Platform;
using Fm4StaticRecomp.Launcher.Core.Profiles;

namespace Fm4StaticRecomp.Launcher.Tests.Updates;

public class PlayPreparationTests
{
    [Fact]
    public void Backs_up_once_per_version_and_only_a_profile_with_saves()
    {
        using var temp = new TempDir();
        var layout = new InstallLayout(temp.Path);
        var store = new ProfileStore(layout, Platforms.Current);
        var fresh = store.Create("Fresh");
        Assert.Null(PlayPreparation.BackupIfNeeded(fresh, "0.1.0", store, layout, Platforms.Current));
        temp.File(Path.Combine("profiles", fresh.Id, "user", "B13EBABEBABEBABE", "save.bin"), [1]);
        var withSave = store.Get(fresh.Id);
        Assert.Null(PlayPreparation.BackupIfNeeded(withSave, "0.1.0", store, layout, Platforms.Current));
        Assert.NotNull(PlayPreparation.BackupIfNeeded(store.Get(fresh.Id), "0.2.0", store, layout, Platforms.Current));
        Assert.Null(PlayPreparation.BackupIfNeeded(store.Get(fresh.Id), "0.2.0", store, layout, Platforms.Current));
        Assert.Single(Directory.GetFiles(layout.Backups));
    }
}
