using Fm4StaticRecomp.Launcher.Core.Install;
using Fm4StaticRecomp.Launcher.Core.Profiles;

namespace Fm4StaticRecomp.Launcher.Tests.Install;

public class SkipLauncherTests
{
    private static readonly Profile Main = new("main", @"C:\G\data\profiles\main", "Main", "Racer", null);
    private static readonly Profile Kid = new("kid", @"C:\G\data\profiles\kid", "Kid's Career", "Junior", null);
    private static readonly Profile[] Both = [Main, Kid];

    [Fact]
    public void Without_the_switch_the_launcher_opens() =>
        Assert.Null(SkipLauncherRequest.Parse(["--after-update"]));

    [Fact]
    public void The_switch_alone_asks_for_no_profile() =>
        Assert.Equal(new SkipLauncherRequest(null), SkipLauncherRequest.Parse(["--skip-launcher"]));

    [Fact]
    public void The_profile_switch_takes_the_next_argument() =>
        Assert.Equal(new SkipLauncherRequest("Kid's Career"),
            SkipLauncherRequest.Parse(["--profile", "Kid's Career", "--skip-launcher"]));

    [Fact]
    public void A_ready_install_plays_the_active_profile() =>
        Assert.Equal(new SkipLauncherStart(Kid, null), new SkipLauncherRequest(null).Decide(StartKind.Ready, Both, "kid"));

    [Fact]
    public void Without_an_active_profile_the_first_plays() =>
        Assert.Equal(new SkipLauncherStart(Main, null), new SkipLauncherRequest(null).Decide(StartKind.Ready, Both, "gone"));

    [Fact]
    public void A_named_profile_plays_whatever_its_case() =>
        Assert.Equal(new SkipLauncherStart(Kid, null), new SkipLauncherRequest("KID'S CAREER").Decide(StartKind.Ready, Both, "main"));

    [Fact]
    public void An_unknown_profile_opens_the_launcher_and_says_so()
    {
        var start = new SkipLauncherRequest("Nobody").Decide(StartKind.Ready, Both, "main");
        Assert.Null(start.Profile);
        Assert.Contains("\"Nobody\"", start.Note);
    }

    [Theory]
    [InlineData(StartKind.Setup)]
    [InlineData(StartKind.BuildVersion)]
    public void Setup_or_a_build_opens_the_launcher_on_its_own_screen(StartKind kind) =>
        Assert.Equal(new SkipLauncherStart(null, null), new SkipLauncherRequest(null).Decide(kind, Both, "main"));
}
