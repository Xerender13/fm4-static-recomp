namespace Fm4StaticRecomp.Launcher.Core.Settings;

public sealed class LauncherSettings
{
    public const int MaximumBuildJobs = 32;
    private readonly string _path;
    private readonly TomlFile _file;

    private LauncherSettings(string path)
    {
        _path = path;
        _file = TomlFile.Load(path);
        ActiveProfile = _file.Get("active_profile");
        ActiveVersion = _file.Get("active_version");
        PreviousVersion = _file.Get("previous_version");
        SkipIntro = _file.Get("skip_intro") != "false";
        AsyncReadbacks = _file.Get("async_readbacks") != "false";
        PresentViaDxgi = _file.Get("present_via_dxgi") == "true";
        Compat = _file.Get("compat") == "true";
        BuildJobs = int.TryParse(_file.Get("build_jobs"), out var jobs) && jobs is >= 0 and <= MaximumBuildJobs ? jobs : 0;
        AcceptedMicrosoftLicence = _file.Get("accepted_microsoft_licence");
        UseVisualStudioInstaller = _file.Get("toolchain_source") == "VisualStudio";
        Language = (GameLanguages.Find(_file.Get("language")) ?? GameLanguages.Default).Code;
        CommandLineOptions = _file.Get("command_line_options")?.Trim() ?? "";
    }

    public string? ActiveProfile { get; set; }
    public string? ActiveVersion { get; set; }
    public string? PreviousVersion { get; set; }
    public bool SkipIntro { get; set; }
    public bool AsyncReadbacks { get; set; }
    public bool PresentViaDxgi { get; set; }
    public bool Compat { get; set; }
    public int BuildJobs { get; set; }
    public string? AcceptedMicrosoftLicence { get; set; }
    public bool UseVisualStudioInstaller { get; set; }
    public string Language { get; set; }
    public string CommandLineOptions { get; set; }

    public static LauncherSettings Load(string path) => new(path);

    public void Save()
    {
        void Text(string key, string? value)
        {
            if (value is null) _file.Remove(key);
            else _file.Set(key, value, quoted: true);
        }
        void Flag(string key, bool value) => _file.Set(key, value ? "true" : "false", quoted: false);
        Text("active_profile", ActiveProfile);
        Text("active_version", ActiveVersion);
        Text("previous_version", PreviousVersion);
        Flag("skip_intro", SkipIntro);
        Flag("async_readbacks", AsyncReadbacks);
        Flag("present_via_dxgi", PresentViaDxgi);
        Flag("compat", Compat);
        _file.Set("build_jobs", BuildJobs.ToString(System.Globalization.CultureInfo.InvariantCulture), quoted: false);
        Text("accepted_microsoft_licence", AcceptedMicrosoftLicence);
        _file.Set("toolchain_source", UseVisualStudioInstaller ? "VisualStudio" : "Feed", quoted: true);
        Text("language", Language);
        Text("command_line_options", CommandLineOptions.Length > 0 ? CommandLineOptions : null);
        _file.Save(_path);
    }
}
