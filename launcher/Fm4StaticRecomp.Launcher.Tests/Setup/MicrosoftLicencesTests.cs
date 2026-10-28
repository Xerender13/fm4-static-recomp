using System.Text;
using Fm4StaticRecomp.Launcher.Core.Platform;
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
        try { licences = await MicrosoftLicences.ReadAsync(script, new WindowsPlatform().ConsoleEncoding, CancellationToken.None); }
        catch (InvalidOperationException) { return; }
        Assert.StartsWith("https://", licences.BuildTools);
        Assert.StartsWith("https://", licences.WindowsSdk);
    }

    // Windows PowerShell writes its errors in the console's code page, a script may switch to UTF-8.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reports_the_script_s_error_readably(bool utf8)
    {
        if (!OperatingSystem.IsWindows()) return;
        var oem = new WindowsPlatform().ConsoleEncoding;
        var text = new[] { "Ошибка сети", "Erreur réseau" }.First(t => oem.GetString(oem.GetBytes(t)) == t);
        using var temp = new TempDir();
        var script = Path.Combine(temp.Path, "fails.ps1");
        File.WriteAllText(script, (utf8 ? "[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false); " : "") +
                                  $"[Console]::Error.WriteLine('{text}'); exit 1", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => MicrosoftLicences.ReadAsync(script, oem, CancellationToken.None));
        Assert.Contains(text, error.Message);
    }
}
