using Fm4StaticRecomp.Launcher.Core.Install;
using Fm4StaticRecomp.Launcher.Core.Setup;

namespace Fm4StaticRecomp.Launcher.Tests.Install;

public class StartDecisionTests
{
    [Fact]
    public void A_new_install_starts_setup() =>
        Assert.Equal(StartKind.Setup, StartDecision.Decide(new SetupState(), 0, "0.1.0"));

    [Fact]
    public void An_unfinished_setup_resumes_setup() =>
        Assert.Equal(StartKind.Setup, StartDecision.Decide(new SetupState { Disc1Release = "1.0 USA" }, 0, "0.1.0"));

    [Fact]
    public void A_new_launcher_version_builds_side_by_side() =>
        Assert.Equal(StartKind.BuildVersion, StartDecision.Decide(
            new SetupState { Disc1Release = "1.0 USA", BuiltVersions = ["0.1.0"] }, 1, "0.2.0"));

    [Fact]
    public void A_built_version_is_ready() =>
        Assert.Equal(StartKind.Ready, StartDecision.Decide(
            new SetupState { Disc1Release = "1.0 USA", BuiltVersions = ["0.1.0", "0.2.0"] }, 1, "0.2.0"));

    [Fact]
    public void After_going_back_the_built_payload_stays_ready() =>
        Assert.Equal(StartKind.Ready, StartDecision.Decide(
            new SetupState { Disc1Release = "1.0 USA", BuiltVersions = ["0.1.0", "0.2.0"] }, 2, "0.2.0"));
}
