using Fm4StaticRecomp.Launcher.Core.Profiles;

namespace Fm4StaticRecomp.Launcher.Core.Install;

// The profile to play without the window, or none and the note the opened window shows.
public sealed record SkipLauncherStart(Profile? Profile, string? Note);

// --skip-launcher [--profile NAME]: play straight from a frontend such as Steam or Pegasus. The
// window opens only when the game cannot start or ends abnormally.
public sealed record SkipLauncherRequest(string? ProfileName)
{
    public const string Switch = "--skip-launcher";
    public const string ProfileSwitch = "--profile";

    public static SkipLauncherRequest? Parse(IReadOnlyList<string> args)
    {
        if (!args.Contains(Switch)) return null;
        var index = args.ToList().IndexOf(ProfileSwitch);
        return new SkipLauncherRequest(index >= 0 && index + 1 < args.Count ? args[index + 1] : null);
    }

    // Setup and a version still to build show their own screens, so they need no note.
    public SkipLauncherStart Decide(StartKind kind, IReadOnlyList<Profile> profiles, string? activeProfileId)
    {
        if (kind != StartKind.Ready) return new SkipLauncherStart(null, null);
        if (ProfileName is { } name)
        {
            var named = profiles.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            return named is null
                ? new SkipLauncherStart(null, $"No profile is named \"{name}\". Pick one and press PLAY.")
                : new SkipLauncherStart(named, null);
        }
        return new SkipLauncherStart(profiles.FirstOrDefault(p => p.Id == activeProfileId) ?? profiles.FirstOrDefault(), null);
    }
}
