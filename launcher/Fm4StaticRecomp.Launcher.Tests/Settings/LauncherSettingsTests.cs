using Fm4StaticRecomp.Launcher.Core.Settings;

namespace Fm4StaticRecomp.Launcher.Tests.Settings;

public class LauncherSettingsTests
{
    [Fact]
    public void A_missing_file_gives_the_defaults()
    {
        using var temp = new TempDir();
        var settings = LauncherSettings.Load(Path.Combine(temp.Path, "launcher.toml"));
        Assert.True(settings.SkipIntro);
        Assert.True(settings.AsyncReadbacks);
        Assert.False(settings.PresentViaDxgi);
        Assert.False(settings.Compat);
        Assert.False(settings.UseVisualStudioInstaller);
        Assert.Equal(0, settings.BuildJobs);
        Assert.Null(settings.ActiveProfile);
        Assert.Null(settings.ActiveVersion);
        Assert.Null(settings.PreviousVersion);
        Assert.Null(settings.AcceptedMicrosoftLicence);
    }

    [Fact]
    public void Round_trips_every_property()
    {
        using var temp = new TempDir();
        var path = Path.Combine(temp.Path, "config", "launcher.toml");
        var settings = LauncherSettings.Load(path);
        settings.ActiveProfile = "main";
        settings.ActiveVersion = "0.2.0";
        settings.PreviousVersion = "0.1.3";
        settings.SkipIntro = false;
        settings.AsyncReadbacks = false;
        settings.PresentViaDxgi = true;
        settings.Compat = true;
        settings.BuildJobs = 6;
        settings.AcceptedMicrosoftLicence = "https://a https://b";
        settings.UseVisualStudioInstaller = true;
        settings.Save();
        var read = LauncherSettings.Load(path);
        Assert.Equal(("main", "0.2.0", "0.1.3"), (read.ActiveProfile, read.ActiveVersion, read.PreviousVersion));
        Assert.Equal((false, false, true, true, true), (read.SkipIntro, read.AsyncReadbacks, read.PresentViaDxgi, read.Compat,
            read.UseVisualStudioInstaller));
        Assert.Equal(6, read.BuildJobs);
        Assert.Equal("https://a https://b", read.AcceptedMicrosoftLicence);
        Assert.Contains("toolchain_source = \"VisualStudio\"", File.ReadAllText(path));
        read.UseVisualStudioInstaller = false;
        read.Save();
        Assert.Contains("toolchain_source = \"Feed\"", File.ReadAllText(path));
    }

    [Fact]
    public void Clears_a_text_set_to_null()
    {
        using var temp = new TempDir();
        var path = Path.Combine(temp.Path, "launcher.toml");
        var settings = LauncherSettings.Load(path);
        settings.ActiveProfile = "main";
        settings.PreviousVersion = "0.1.3";
        settings.Save();
        settings.ActiveProfile = null;
        settings.PreviousVersion = null;
        settings.Save();
        var read = LauncherSettings.Load(path);
        Assert.Null(read.ActiveProfile);
        Assert.Null(read.PreviousVersion);
        Assert.DoesNotContain("active_profile", File.ReadAllText(path));
    }

    [Fact]
    public void A_build_job_count_out_of_range_reads_as_automatic()
    {
        using var temp = new TempDir();
        var settings = LauncherSettings.Load(temp.File("launcher.toml", "build_jobs = 99\n"u8.ToArray()));
        Assert.Equal(0, settings.BuildJobs);
    }

    [Fact]
    public void Keeps_keys_it_does_not_know()
    {
        using var temp = new TempDir();
        var path = temp.File("launcher.toml", "future_key = 3\n"u8.ToArray());
        var settings = LauncherSettings.Load(path);
        settings.Compat = true;
        settings.Save();
        Assert.StartsWith("future_key = 3\n", File.ReadAllText(path));
    }
}
