using Fm4StaticRecomp.Launcher.Core.Install;

namespace Fm4StaticRecomp.Launcher.Tests.Install;

public class InstallLayoutTests
{
    [Fact]
    public void Lives_in_data_beside_the_launcher()
    {
        if (!OperatingSystem.IsWindows()) return;
        var layout = InstallLayout.ForLauncher(@"C:\Games\FM4\");
        Assert.Equal(@"C:\Games\FM4\data", layout.Root);
        Assert.Equal(@"C:\Games\FM4\data\source\0.2.0\out\build\win-amd64-release\fm4_static_recomp.exe",
            layout.GameExecutable("0.2.0"));
        Assert.Equal(@"C:\Games\FM4\data\cache\0.2.0\ui-fonts", layout.UiFonts("0.2.0"));
        Assert.Equal(@"C:\Games\FM4\data\reports", layout.Reports);
        Assert.Equal(@"C:\Games\FM4\data\source\0.2.0\.local\logs\setup-error.json", layout.SetupFailureRecord("0.2.0"));
    }

    [Fact]
    public void Names_every_folder_of_the_spec()
    {
        if (!OperatingSystem.IsWindows()) return;
        var layout = new InstallLayout(@"C:\G\data");
        Assert.Equal(
            [@"C:\G\data\game", @"C:\G\data\content", @"C:\G\data\toolchain", @"C:\G\data\update", @"C:\G\data\logs",
             @"C:\G\data\logs\runtime.log", @"C:\G\data\logs\launcher.log", @"C:\G\data\downloads", @"C:\G\data\config",
             @"C:\G\data\config\graphics.toml", @"C:\G\data\config\launcher.toml", @"C:\G\data\config\backups",
             @"C:\G\data\backups", @"C:\G\data\profiles", @"C:\G\data\deleted", @"C:\G\data\setup-state.json",
             @"C:\G\data\content-install\user"],
            [layout.Game, layout.Content, layout.Toolchain, layout.Update, layout.Logs, layout.RuntimeLog, layout.LauncherLog,
             layout.Downloads, layout.Config, layout.GraphicsToml, layout.LauncherToml, layout.ConfigBackups, layout.Backups,
             layout.Profiles, layout.Deleted, layout.SetupState, layout.ContentInstallUser]);
        Assert.Equal(@"C:\G\data\source\1.0.0\tools\setup.ps1", layout.SetupScript("1.0.0"));
        Assert.Equal(@"C:\G\data\source\1.0.0\tools\microsoft-licences.ps1", layout.LicencesScript("1.0.0"));
        Assert.Equal(@"C:\G\data\source\1.0.0\tools\create-crash-report.ps1", layout.CrashReportScript("1.0.0"));
        Assert.Equal(@"C:\G\data\source\1.0.0\.local\logs", layout.SetupLogs("1.0.0"));
        Assert.Equal(@"C:\G\data\source\1.0.0\config\supported-discs.json", layout.DiscManifestOf("1.0.0"));
        Assert.Equal(@"C:\G\data\source\1.0.0\out\build\win-amd64-release\graphics-settings.json", layout.GraphicsSchema("1.0.0"));
    }
}
