namespace Fm4StaticRecomp.Launcher.Core.Profiles;

public sealed record Profile(string Id, string Folder, string Name, string Gamertag, string? LastBackedUpVersion)
{
    public string User => Path.Combine(Folder, "user");
}
