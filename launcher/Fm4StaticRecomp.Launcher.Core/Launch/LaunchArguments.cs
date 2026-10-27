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
        return options.Select(o => $"--{o.Key}={o.Value}").ToList();
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
