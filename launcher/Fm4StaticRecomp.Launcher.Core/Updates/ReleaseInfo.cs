namespace Fm4StaticRecomp.Launcher.Core.Updates;

public sealed record ReleaseInfo(Version Version, string Tag, bool Prerelease, string Notes, IReadOnlyDictionary<string, Uri> Assets);
