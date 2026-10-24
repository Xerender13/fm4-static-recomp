using Fm4StaticRecomp.Launcher.Core.Install;
using Fm4StaticRecomp.Launcher.Core.Launch;
using Fm4StaticRecomp.Launcher.Core.Platform;
using Fm4StaticRecomp.Launcher.Core.Profiles;
using Fm4StaticRecomp.Launcher.Core.Settings;

namespace Fm4StaticRecomp.Launcher.Tests.Launch;

public class LaunchArgumentsTests
{
    private static (InstallLayout, Profile, LauncherSettings, TempDir) Setup()
    {
        var temp = new TempDir();
        var layout = new InstallLayout(temp.Path);
        var profile = new Profile("main", Path.Combine(layout.Profiles, "main"), "Main", "Racer", null);
        return (layout, profile, LauncherSettings.Load(layout.LauncherToml), temp);
    }

    [Fact]
    public void Play_points_the_game_at_the_profile_and_shared_folders()
    {
        var (layout, profile, settings, temp) = Setup();
        using var _ = temp;
        var args = LaunchArguments.ForPlay(layout, "0.1.0", profile, settings, new WindowsPlatform());
        Assert.Contains($"--game_data_root={layout.Game}", args);
        Assert.Contains($"--user_data_root={profile.User}", args);
        Assert.Contains($"--cache_root={layout.Cache("0.1.0")}", args);
        Assert.Contains($"--fm4_config_root={layout.Config}", args);
        Assert.Contains("--user_name=Racer", args);
        Assert.Contains($"--update_data_root={layout.Update}", args);
        Assert.Contains($"--log_file={layout.RuntimeLog}", args);
        Assert.Contains("--fm4_skip_intro=true", args);
        Assert.DoesNotContain(args, a => a.StartsWith("--fh1_async_readbacks"));
    }

    [Fact]
    public void Skip_intro_off_leaves_the_option_out()
    {
        var (layout, profile, settings, temp) = Setup();
        using var _ = temp;
        settings.SkipIntro = false;
        var args = LaunchArguments.ForPlay(layout, "0.1.0", profile, settings, new WindowsPlatform());
        Assert.DoesNotContain(args, a => a.StartsWith("--fm4_skip_intro"));
    }

    [Fact]
    public void Never_repeats_an_option()
    {
        var (layout, profile, settings, temp) = Setup();
        using var _ = temp;
        settings.Compat = true;
        settings.AsyncReadbacks = false;
        settings.PresentViaDxgi = true;
        var args = LaunchArguments.ForPlay(layout, "0.1.0", profile, settings, new WindowsPlatform());
        var names = args.Select(a => a.Split('=')[0]).ToList();
        Assert.Equal(names.Count, names.Distinct().Count());
        Assert.Contains("--fh1_async_readbacks=false", args);
        Assert.Contains("--vulkan_present_via_dxgi=true", args);
    }

    [Fact]
    public void Dxgi_is_dropped_where_unsupported()
    {
        var (layout, profile, settings, temp) = Setup();
        using var _ = temp;
        settings.PresentViaDxgi = true;
        var args = LaunchArguments.ForPlay(layout, "0.1.0", profile, settings, new LinuxPlatform());
        Assert.DoesNotContain(args, a => a.StartsWith("--vulkan_present_via_dxgi"));
    }
}
