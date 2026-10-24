using Fm4StaticRecomp.Launcher.Core.Install;
using Fm4StaticRecomp.Launcher.Core.Platform;
using Fm4StaticRecomp.Launcher.Core.Profiles;

namespace Fm4StaticRecomp.Launcher.Core.Updates;

public static class PlayPreparation
{
    // Before a version's first launch on a profile: a new build could damage a save.
    public static string? BackupIfNeeded(Profile profile, string version, ProfileStore store, InstallLayout layout,
        IPlatform platform)
    {
        if (profile.LastBackedUpVersion == version) return null;
        var hasSaves = Directory.Exists(profile.User) &&
                       Directory.GetDirectories(profile.User).Any(d => !platform.IsDirectoryLink(d));
        var zip = hasSaves ? SaveBackup.Create(profile, layout.Backups, version, platform) : null;
        store.MarkBackedUp(profile.Id, version);
        return zip;
    }
}
