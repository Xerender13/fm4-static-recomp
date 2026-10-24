using Fm4StaticRecomp.Launcher.Core.Setup;

namespace Fm4StaticRecomp.Launcher.Core.Install;

public enum StartKind { Setup, BuildVersion, Ready }

public static class StartDecision
{
    // Ready whenever the payload's version is built, active or not: after "Go back" nothing
    // switches by itself.
    public static StartKind Decide(SetupState state, int profileCount, string payloadVersion)
    {
        if (!state.Disc1Installed || state.BuiltVersions.Count == 0 || profileCount == 0) return StartKind.Setup;
        return state.BuiltVersions.Contains(payloadVersion) ? StartKind.Ready : StartKind.BuildVersion;
    }
}
