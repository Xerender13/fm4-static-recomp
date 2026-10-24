namespace Fm4StaticRecomp.Launcher.Core.Setup;

public sealed record SetupRequest(string Version, int BuildJobs, bool AcceptMicrosoftLicence, bool UseVisualStudioInstaller);

public sealed record SetupFailure(string Message, string? Step, int ExitCode, IReadOnlyList<string> Excerpt,
    string? Hint, string? BuildLog, string LogFolder)
{
    public bool OffersVisualStudioInstaller => Step is "licence" or "msvc" or "windows-sdk";
}

public interface IBuildRunner
{
    Task<SetupFailure?> BuildAsync(SetupRequest request, IProgress<SetupEvent> progress, Action<string> logLine,
        CancellationToken cancellation);
}
