using System.IO.Compression;
using Fm4StaticRecomp.Launcher.Core.Platform;
using Fm4StaticRecomp.Launcher.Core.Profiles;
using Fm4StaticRecomp.Launcher.Core.Updates;

namespace Fm4StaticRecomp.Launcher.Tests.Updates;

public class SaveBackupTests
{
    [Fact]
    public void Zips_the_save_without_following_the_content_link()
    {
        using var temp = new TempDir();
        temp.File("content/4D530910/00000002/pack/huge.bin", new byte[1000]);
        var profile = new Profile("main", Path.Combine(temp.Path, "profile"), "Main", "Racer", null);
        temp.File("profile/user/B13EBABEBABEBABE/4D530910/00000001/ForzaProfile/ForzaProfile", [7]);
        Platforms.Current.CreateDirectoryLink(Path.Combine(profile.User, ProfileStore.ContentFolderName),
            Path.Combine(temp.Path, "content"));
        var zip = SaveBackup.Create(profile, Path.Combine(temp.Path, "backups"), "0.2.0", Platforms.Current);
        using var archive = ZipFile.OpenRead(zip);
        Assert.Contains(archive.Entries, e => e.FullName == "B13EBABEBABEBABE/4D530910/00000001/ForzaProfile/ForzaProfile");
        Assert.DoesNotContain(archive.Entries, e => e.FullName.Contains("huge.bin"));
    }

    [Fact]
    public void Keeps_the_last_three_per_profile()
    {
        using var temp = new TempDir();
        var backups = Path.Combine(temp.Path, "backups");
        for (var i = 0; i < 5; i++)
        {
            var file = temp.File($"backups/main-0.{i}.0-2026101{i}-000000.zip", [1]);
            File.SetLastWriteTimeUtc(file, new DateTime(2026, 10, 10 + i, 0, 0, 0, DateTimeKind.Utc));
        }
        temp.File("backups/other-0.1.0-20261001-000000.zip", [1]);
        SaveBackup.Prune(backups, "main");
        Assert.Equal(4, Directory.GetFiles(backups).Length);
        Assert.False(File.Exists(Path.Combine(backups, "main-0.0.0-20261010-000000.zip")));
    }
}
