using Fm4StaticRecomp.Launcher.Core.Install;
using Fm4StaticRecomp.Launcher.Core.Platform;
using Fm4StaticRecomp.Launcher.Core.Profiles;
using Fm4StaticRecomp.Launcher.Core.Settings;

namespace Fm4StaticRecomp.Launcher.Core.Launch;

public static class LaunchArguments
{
    // The list of tools\run.ps1 -Compat, its source of truth: every rendering shortcut that has a switch off.
    private static readonly (string Name, string Value)[] CompatOptions =
    [
        ("fh1_async_readbacks", "false"), ("fh1_readback_all_resolves", "true"),
        ("fh1_surfaceless_clears", "false"), ("fh1_color_overwrite_claims", "false"),
        ("fh1_scrambled_stencil_claims", "false"), ("fh1_scaled_msaa_single_sample", "false"),
        ("gpu_record_thread", "false"), ("clear_memory_page_state", "true"),
        ("texture_cache_memory_limit_soft", "1024"), ("anisotropic_override", "-1"),
        ("fm4_level_of_detail", "console"), ("fm4_shadow_map_size", "512"),
        ("fm4_ground_shadow_size", "256"),
        ("fm4_reflection_cube_size", "256"), ("fm4_full_rate_scenes", "false"),
    ];

    public static IReadOnlyList<string> ForPlay(InstallLayout layout, string version, Profile profile,
        LauncherSettings settings, IPlatform platform)
    {
        var options = new Dictionary<string, string>
        {
            ["game_data_root"] = layout.Game,
            ["user_data_root"] = profile.User,
            ["update_data_root"] = layout.Update,
            ["cache_root"] = layout.Cache(version),
            ["log_file"] = layout.RuntimeLog,
            ["fm4_config_root"] = layout.Config,
            ["user_name"] = profile.Gamertag,
            ["fm4_language"] = settings.Language,
        };
        if (settings.SkipIntro) options["fm4_skip_intro"] = "true";
        if (settings.Compat) foreach (var (name, value) in CompatOptions) options[name] = value;
        if (!settings.AsyncReadbacks) options["fh1_async_readbacks"] = "false";
        if (settings.PresentViaDxgi && platform.SupportsDxgiPresentation) options["vulkan_present_via_dxgi"] = "true";
        var args = options.Select(o => $"--{o.Key}={o.Value}").ToList();
        // A COMMAND-LINE OPTIONS entry replaces the launcher's value, never repeats it: the SDK drops every
        // argument when one is given twice.
        foreach (var option in SplitCommandLine(settings.CommandLineOptions))
        {
            var name = NameOf(option);
            args.RemoveAll(a => NameOf(a) == name);
            args.Add(option);
        }
        return args;
    }

    // The SDK reads --no-name as --name=false, the same option.
    private static string NameOf(string argument) =>
        argument.Split('=', 2)[0] is var name && name.StartsWith("--no-", StringComparison.Ordinal) ? "--" + name[5..] : name;

    // Splits at spaces outside double quotes and drops the quotes, so --opt="C:\a b" is one argument.
    // A backslash is a path separator here, not an escape.
    private static IReadOnlyList<string> SplitCommandLine(string text)
    {
        var args = new List<string>();
        var current = new System.Text.StringBuilder();
        var quoted = false;
        var started = false;
        foreach (var c in text)
        {
            if (c == '"') (quoted, started) = (!quoted, true);
            else if (char.IsWhiteSpace(c) && !quoted)
            {
                if (started) args.Add(current.ToString());
                (current, started) = (new(), false);
            }
            else (current, started) = (current.Append(c), true);
        }
        if (started) args.Add(current.ToString());
        return args;
    }

    public static IReadOnlyList<string> ForContentInstall(InstallLayout layout, string? disc2Folder,
        string? dlcFolder, string logFile)
    {
        var args = new List<string>
        {
            $"--game_data_root={layout.Game}",
            $"--user_data_root={layout.ContentInstallUser}",
            $"--log_file={logFile}",
            "--fm4_install_only=true",
        };
        if (disc2Folder is not null) args.Add($"--fm4_disc2_packages={disc2Folder}");
        if (dlcFolder is not null) args.Add($"--fm4_dlc_packages={dlcFolder}");
        return args;
    }
}
