using System.IO.Compression;
using System.Text.Json;
using Fm4StaticRecomp.Launcher.Core.Discs;
using Fm4StaticRecomp.Launcher.Core.Updates;

namespace Fm4StaticRecomp.Launcher.Core.Install;

// The source zip that ships beside the launcher; its config/release.json names the version
// and the repository updates come from.
public sealed record PayloadInfo(string ZipPath, string Version, string Repository)
{
    public const string FileName = "fm4-static-recomp-source.zip";

    public string OwnerRepo => ReleaseClient.OwnerRepo(Repository);

    public static PayloadInfo Read(string launcherDirectory, string launcherVersion)
    {
        var zip = Path.Combine(launcherDirectory, FileName);
        if (!File.Exists(zip))
            throw new FileNotFoundException($"Keep {FileName} beside the launcher; it is part of the release zip.", zip);
        using var archive = ZipFile.OpenRead(zip);
        var entry = archive.GetEntry("config/release.json")
                    ?? throw new InvalidDataException($"{FileName} has no config/release.json.");
        using var stream = entry.Open();
        using var document = JsonDocument.Parse(stream);
        var version = document.RootElement.GetProperty("version").GetString() ?? "";
        var repository = document.RootElement.GetProperty("repository").GetString() ?? "";
        if (version != launcherVersion)
            throw new InvalidDataException(
                $"{FileName} beside the launcher is version {version}, the launcher is {launcherVersion}. " +
                "Extract both files of the same release zip.");
        return new PayloadInfo(zip, version, repository);
    }

    public string ExtractEntry(string entry, string destinationFolder)
    {
        using var archive = ZipFile.OpenRead(ZipPath);
        var found = archive.GetEntry(entry) ?? throw new FileNotFoundException($"{FileName} has no {entry}.", entry);
        var target = SafePath.Combine(destinationFolder, entry);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        found.ExtractToFile(target, overwrite: true);
        return target;
    }
}
