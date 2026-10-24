using System.Security.Cryptography;
using Fm4StaticRecomp.Launcher.Core.Discs;

namespace Fm4StaticRecomp.Launcher.Core.Updates;

// What an update does to game\ (spec 13): only default.xex is re-checked, and the disc is
// extracted again only when the new manifest asks for files the old one did not.
public static class GameFolderCheck
{
    public static bool NeedsExtraction(DiscManifest installed, DiscManifest next, string releaseName)
    {
        if (Release(next, releaseName) is not { } nextRelease || Release(installed, releaseName) is not { } installedRelease)
            return true;
        var known = installed.Disc1Files(installedRelease).Select(f => f.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return next.Disc1Files(nextRelease).Any(f => !f.Optional && !known.Contains(f.Path));
    }

    public static bool ExecutableMatches(string gameFolder, DiscManifest manifest, string releaseName)
    {
        var expected = Release(manifest, releaseName)?.Files
            .FirstOrDefault(f => string.Equals(f.Path, Disc1Installer.Executable, StringComparison.OrdinalIgnoreCase));
        var path = Path.Combine(gameFolder, Disc1Installer.Executable);
        if (expected is null || !File.Exists(path) || new FileInfo(path).Length != expected.Size) return false;
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).Equals(expected.Sha256, StringComparison.OrdinalIgnoreCase);
    }

    private static Disc1Release? Release(DiscManifest manifest, string name) =>
        manifest.Disc1.Releases.FirstOrDefault(r => r.Name == name);
}
