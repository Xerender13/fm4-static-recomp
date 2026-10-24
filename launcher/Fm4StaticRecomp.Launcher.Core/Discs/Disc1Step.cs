namespace Fm4StaticRecomp.Launcher.Core.Discs;

public abstract record Disc1Outcome
{
    public sealed record Installed(string ReleaseName, int ProblemsAccepted) : Disc1Outcome;
    public sealed record Refused(RequiredCheck Check) : Disc1Outcome;
    public sealed record Declined : Disc1Outcome;
}

public static class Disc1Step
{
    // Extracts into <game>.partial and renames it only when the player has accepted the
    // result, so an interrupted or declined install never looks installed.
    public static async Task<Disc1Outcome> InstallAsync(DiscInput input, DiscManifest manifest, string gameFolder,
        Func<IReadOnlyList<FileProblem>, Task<bool>> acceptProblems, IProgress<double>? progress,
        CancellationToken cancellation)
    {
        gameFolder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(gameFolder));
        using var files = input.OpenFiles();
        var check = await Disc1Installer.CheckRequiredAsync(files, manifest, input.HeaderMediaId, cancellation);
        if (!check.Matches) return new Disc1Outcome.Refused(check);
        var partial = gameFolder + ".partial";
        if (Directory.Exists(partial)) Directory.Delete(partial, recursive: true);
        try
        {
            var problems = await Task.Run(() => Disc1Installer.ExtractAsync(files, manifest, check.Release!, partial, progress,
                cancellation), cancellation);
            if (problems.Count > 0 && !await acceptProblems(problems))
            {
                Directory.Delete(partial, recursive: true);
                return new Disc1Outcome.Declined();
            }
            cancellation.ThrowIfCancellationRequested();
            Swap(partial, gameFolder);
            return new Disc1Outcome.Installed(check.Release!.Name, problems.Count);
        }
        catch
        {
            if (Directory.Exists(partial)) Directory.Delete(partial, recursive: true);
            throw;
        }
    }

    // The launcher calls this at start: a replacement cut short between its two renames
    // leaves the old game in <game>.old.
    public static void RecoverInterruptedReplace(string gameFolder)
    {
        var old = Path.TrimEndingDirectorySeparator(Path.GetFullPath(gameFolder)) + ".old";
        if (!Directory.Exists(old)) return;
        if (Directory.Exists(gameFolder)) DeleteQuietly(old);
        else Directory.Move(old, gameFolder);
    }

    // Renames instead of deleting first: a file held open by a virus scanner must not leave a
    // half-deleted game that setup-state.json still counts as installed.
    private static void Swap(string partial, string gameFolder)
    {
        var old = gameFolder + ".old";
        if (Directory.Exists(gameFolder))
        {
            if (Directory.Exists(old)) Directory.Delete(old, recursive: true);
            Directory.Move(gameFolder, old);
        }
        try
        {
            Directory.Move(partial, gameFolder);
        }
        catch
        {
            if (Directory.Exists(old) && !Directory.Exists(gameFolder)) Directory.Move(old, gameFolder);
            throw;
        }
        DeleteQuietly(old);
    }

    private static void DeleteQuietly(string folder)
    {
        try { Directory.Delete(folder, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
