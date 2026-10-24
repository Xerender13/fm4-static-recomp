namespace Fm4StaticRecomp.Launcher.Core.Install;

public sealed record InstallLayout(string Root)
{
    public static InstallLayout ForLauncher(string launcherDirectory) =>
        new(Path.Combine(Path.TrimEndingDirectorySeparator(Path.GetFullPath(launcherDirectory)), "data"));

    public string Game => Path.Combine(Root, "game");
    public string Content => Path.Combine(Root, "content");
    public string Toolchain => Path.Combine(Root, "toolchain");
    public string Update => Path.Combine(Root, "update");
    public string Logs => Path.Combine(Root, "logs");
    public string RuntimeLog => Path.Combine(Logs, "runtime.log");
    public string LauncherLog => Path.Combine(Logs, "launcher.log");
    public string Downloads => Path.Combine(Root, "downloads");
    public string Config => Path.Combine(Root, "config");
    public string GraphicsToml => Path.Combine(Config, "graphics.toml");
    public string LauncherToml => Path.Combine(Config, "launcher.toml");
    public string ConfigBackups => Path.Combine(Config, "backups");
    public string Backups => Path.Combine(Root, "backups");
    public string Profiles => Path.Combine(Root, "profiles");
    public string Deleted => Path.Combine(Root, "deleted");
    public string SetupState => Path.Combine(Root, "setup-state.json");
    public string ContentInstallUser => Path.Combine(Root, "content-install", "user");
    public string Reports => Path.Combine(Root, "reports");

    public string Source(string version) => Path.Combine(Root, "source", version);
    public string Cache(string version) => Path.Combine(Root, "cache", version);
    public string UiFonts(string version) => Path.Combine(Cache(version), "ui-fonts");
    // The Windows preset folder and .exe are the layout's one Windows assumption (spec 14).
    public string BuildFolder(string version) => Path.Combine(Source(version), "out", "build", "win-amd64-release");
    public string GameExecutable(string version) => Path.Combine(BuildFolder(version), "fm4_static_recomp.exe");
    public string GraphicsSchema(string version) => Path.Combine(BuildFolder(version), "graphics-settings.json");
    public string SetupScript(string version) => Path.Combine(Source(version), "tools", "setup.ps1");
    public string LicencesScript(string version) => Path.Combine(Source(version), "tools", "microsoft-licences.ps1");
    public string CrashReportScript(string version) => Path.Combine(Source(version), "tools", "create-crash-report.ps1");
    public string SetupLogs(string version) => Path.Combine(Source(version), ".local", "logs");
    public string SetupFailureRecord(string version) => Path.Combine(SetupLogs(version), "setup-error.json");
    public string DiscManifestOf(string version) => Path.Combine(Source(version), "config", "supported-discs.json");
}
