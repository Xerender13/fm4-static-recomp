using Fm4StaticRecomp.Launcher.Core.Settings;
using Fm4StaticRecomp.Launcher.Core.Setup;

namespace Fm4StaticRecomp.Launcher.Tests.Setup;

public class MicrosoftLicencesTests
{
    [Fact]
    public void Parses_the_script_s_line_and_compares_with_the_accepted_one()
    {
        var licences = MicrosoftLicences.Parse("""{"build_tools":"https://go.microsoft.com/fwlink/?LinkId=2179911","windows_sdk":"https://aka.ms/WinSDKLicenseURL"}""");
        using var temp = new TempDir();
        var settings = LauncherSettings.Load(Path.Combine(temp.Path, "launcher.toml"));
        Assert.False(licences.AcceptedIn(settings));
        settings.AcceptedMicrosoftLicence = licences.Key;
        Assert.True(licences.AcceptedIn(settings));
        Assert.False((licences with { WindowsSdk = "https://example.test/new" }).AcceptedIn(settings));
    }

    [Theory]
    [InlineData("http://go.microsoft.com/fwlink/?LinkId=2179911")]
    [InlineData("file:///C:/Windows/System32/calc.exe")]
    [InlineData("C:/Windows/System32/calc.exe")]
    [InlineData("go.microsoft.com/fwlink")]
    public void Refuses_a_licence_that_is_not_an_https_link(string link) =>
        Assert.Throws<InvalidDataException>(() =>
            MicrosoftLicences.Parse($$"""{"build_tools":"{{link}}","windows_sdk":"https://aka.ms/WinSDKLicenseURL"}"""));

    [Fact]
    public async Task Reads_the_real_script()
    {
        if (!OperatingSystem.IsWindows()) return;
        var script = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\..\tools\microsoft-licences.ps1"));
        MicrosoftLicences licences;
        // The script reads Microsoft's channel; without network it exits non-zero.
        try { licences = await MicrosoftLicences.ReadAsync(script, CancellationToken.None); }
        catch (InvalidOperationException) { return; }
        Assert.StartsWith("https://", licences.BuildTools);
        Assert.StartsWith("https://", licences.WindowsSdk);
    }
}
