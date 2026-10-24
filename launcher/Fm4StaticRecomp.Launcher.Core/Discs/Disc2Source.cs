using System.Security.Cryptography;

namespace Fm4StaticRecomp.Launcher.Core.Discs;

public sealed record Disc2Packages(string Folder, IReadOnlyList<FileProblem> Problems)
{
    // Differing packages the player accepted still install; a missing one leaves Disc 2 incomplete.
    public bool Complete => Problems.All(p => p.Kind != FileProblemKind.Missing);
}

public static class Disc2Source
{
    // Hashes the four packages where they are; nothing is written.
    public static Task<IReadOnlyList<FileProblem>> VerifyAsync(DiscInput input, DiscManifest manifest,
        IProgress<double>? progress, CancellationToken cancellation) =>
        Task.Run(() => CheckAsync(input, manifest, null, progress, cancellation), cancellation);

    // Copies the packages under lower-case names into a staging folder, ready for
    // --fm4_disc2_packages; the player's files are never modified.
    public static async Task<Disc2Packages> StageAsync(DiscInput input, DiscManifest manifest, string stagingFolder,
        CancellationToken cancellation)
    {
        if (Directory.Exists(stagingFolder)) Directory.Delete(stagingFolder, recursive: true);
        Directory.CreateDirectory(stagingFolder);
        var problems = await Task.Run(() => CheckAsync(input, manifest, stagingFolder, null, cancellation), cancellation);
        return new Disc2Packages(stagingFolder, problems);
    }

    private static async Task<IReadOnlyList<FileProblem>> CheckAsync(DiscInput input, DiscManifest manifest, string? copyTo,
        IProgress<double>? progress, CancellationToken cancellation)
    {
        using var files = input.Kind == DiscInputKind.PackageFolder ? new FolderDiscFiles(input.Path) : input.OpenFiles();
        var onDisc = input.Kind is DiscInputKind.Iso or DiscInputKind.God;
        var packages = manifest.Disc2.Packages.OrderBy(p => p.Name, StringComparer.Ordinal).ToList();
        long total = Math.Max(1, packages.Sum(p => p.Size)), done = 0;
        var problems = new List<FileProblem>();
        var buffer = new byte[1 << 20];
        foreach (var package in packages)
        {
            cancellation.ThrowIfCancellationRequested();
            var found = Locate(files, package.Name, onDisc);
            if (found is null || found.Size != package.Size)
            {
                problems.Add(new FileProblem(package.Name, found is null ? FileProblemKind.Missing : FileProblemKind.SizeMismatch));
                done += package.Size;
                continue;
            }
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var source = files.Open(found))
            await using (var target = copyTo is null ? null : File.Create(Path.Combine(copyTo, package.Name.ToLowerInvariant())))
            {
                int read;
                while ((read = await source.ReadAsync(buffer, cancellation)) > 0)
                {
                    hash.AppendData(buffer, 0, read);
                    if (target is not null) await target.WriteAsync(buffer.AsMemory(0, read), cancellation);
                    done += read;
                    progress?.Report((double)done / total);
                }
            }
            if (!Convert.ToHexString(hash.GetHashAndReset()).Equals(package.Sha256, StringComparison.OrdinalIgnoreCase))
                problems.Add(new FileProblem(package.Name, FileProblemKind.HashMismatch));
        }
        return problems;
    }

    private static DiscFile? Locate(IDiscFiles files, string name, bool onDisc)
    {
        foreach (var file in files.Files)
        {
            if (!string.Equals(file.Path[(file.Path.LastIndexOf('/') + 1)..], name, StringComparison.OrdinalIgnoreCase)) continue;
            if (!onDisc) return file;
            using var stream = files.Open(file);
            if (PackageHeader.TryRead(stream) is { TitleId: PackageHeader.Fm4TitleId }) return file;
        }
        return null;
    }
}
