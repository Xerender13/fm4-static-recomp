using Fm4StaticRecomp.Launcher.Core.Install;

namespace Fm4StaticRecomp.Launcher.Core.Updates;

public sealed record StagedUpdate(Version Version, string LauncherExe, string SourceZip);

public sealed class UpdateCoordinator(ReleaseClient releases, Downloader downloader, InstallLayout layout, Version current)
{
    public async Task<ReleaseInfo?> CheckAsync(CancellationToken cancellation) =>
        await releases.NewestAsync(cancellation) is { } newest && newest.Version > current ? newest : null;

    public async Task<StagedUpdate> DownloadAsync(ReleaseInfo release, IProgress<double>? progress, CancellationToken cancellation)
    {
        foreach (var name in new[] { ReleaseClient.LauncherAsset, ReleaseClient.SourceAsset, ReleaseClient.SumsAsset })
            if (!release.Assets.ContainsKey(name))
                throw new InvalidDataException($"The release {release.Tag} has no {name}.");
        var sums = await releases.SumsAsync(release, cancellation);
        var folder = DownloadFolder(layout, release.Version);
        var exe = Path.Combine(folder, ReleaseClient.LauncherAsset);
        var zip = Path.Combine(folder, ReleaseClient.SourceAsset);
        var exeProgress = progress is null ? null : new Progress<double>(p => progress.Report(p * 0.8));
        var zipProgress = progress is null ? null : new Progress<double>(p => progress.Report(0.8 + p * 0.2));
        await downloader.DownloadAsync(release.Assets[ReleaseClient.LauncherAsset], exe, Sum(sums, ReleaseClient.LauncherAsset), exeProgress, cancellation);
        await downloader.DownloadAsync(release.Assets[ReleaseClient.SourceAsset], zip, Sum(sums, ReleaseClient.SourceAsset), zipProgress, cancellation);
        return new StagedUpdate(release.Version, exe, zip);
    }

    // The source zip goes beside the launcher, where the restarted launcher reads it; the old
    // one is kept as .old until then, like the executable.
    public static void Install(StagedUpdate staged, string launcherExe, string launcherDirectory)
    {
        var zip = Path.Combine(launcherDirectory, PayloadInfo.FileName);
        var oldZip = zip + ".old";
        if (File.Exists(oldZip)) File.Delete(oldZip);
        if (File.Exists(zip)) File.Move(zip, oldZip);
        try
        {
            File.Copy(staged.SourceZip, zip);
            SelfReplace.Replace(launcherExe, staged.LauncherExe);
        }
        catch
        {
            if (File.Exists(zip)) File.Delete(zip);
            if (File.Exists(oldZip)) File.Move(oldZip, zip);
            throw;
        }
    }

    // Spec 13: when the new launcher cannot be started, the old one comes back, with its source.
    public static void Undo(string launcherExe, string launcherDirectory)
    {
        Restore(launcherExe);
        Restore(Path.Combine(launcherDirectory, PayloadInfo.FileName));
    }

    public static string DownloadFolder(InstallLayout layout, Version version) => Path.Combine(layout.Downloads, version.ToString(3));

    // Run by the new launcher when it starts with SelfReplace.AfterUpdateSwitch; downloadFolder
    // is its own release's, already copied beside it.
    public static void CleanUp(string launcherExe, string launcherDirectory, string downloadFolder)
    {
        SelfReplace.CleanUp(launcherExe);
        var oldZip = Path.Combine(launcherDirectory, PayloadInfo.FileName) + ".old";
        try
        {
            if (File.Exists(oldZip)) File.Delete(oldZip);
            if (Directory.Exists(downloadFolder)) Directory.Delete(downloadFolder, recursive: true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static void Restore(string path)
    {
        var old = path + ".old";
        if (!File.Exists(old)) return;
        if (File.Exists(path)) File.Delete(path);
        File.Move(old, path);
    }

    private static string Sum(IReadOnlyDictionary<string, string> sums, string name) =>
        sums.TryGetValue(name, out var sum) ? sum : throw new InvalidDataException($"SHA256SUMS.txt has no line for {name}.");
}
