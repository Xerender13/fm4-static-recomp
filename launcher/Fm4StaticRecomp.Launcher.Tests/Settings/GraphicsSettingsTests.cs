using Fm4StaticRecomp.Launcher.Core.Settings;

namespace Fm4StaticRecomp.Launcher.Tests.Settings;

public class GraphicsSettingsTests
{
    private static GraphicsSchema Schema() =>
        GraphicsSchema.Load(Path.Combine(AppContext.BaseDirectory, "Settings", "schema-sample.json"));

    [Fact]
    public void Uses_defaults_for_missing_or_invalid_values()
    {
        using var temp = new TempDir();
        var toml = temp.File("graphics.toml", "resolution_scale = 9\nfm4_frame_rate = 150\n"u8.ToArray());
        var settings = new GraphicsSettings(Schema(), toml, Path.Combine(temp.Path, "backups"));
        Assert.Equal("1", settings.Value("resolution_scale"));
        Assert.Equal("150", settings.Value("fm4_frame_rate"));
        Assert.Equal("none", settings.Value("swap_post_effect"));
    }

    [Fact]
    public void Matches_a_preset_or_reports_custom()
    {
        using var temp = new TempDir();
        var settings = new GraphicsSettings(Schema(), Path.Combine(temp.Path, "g.toml"), Path.Combine(temp.Path, "b"));
        Assert.Null(settings.CurrentPreset());
        settings.ApplyPreset("HIGH");
        Assert.Equal("HIGH", settings.CurrentPreset());
        settings.Set("swap_post_effect", "none");
        Assert.Null(settings.CurrentPreset());
    }

    [Fact]
    public void Save_keeps_other_keys_and_changes_the_game_made_meanwhile()
    {
        using var temp = new TempDir();
        var toml = temp.File("graphics.toml", "fm4_frame_rate = 120\nunknown = 1\n"u8.ToArray());
        var settings = new GraphicsSettings(Schema(), toml, Path.Combine(temp.Path, "backups"));
        File.WriteAllText(toml, "fm4_frame_rate = 144\nunknown = 1\nresolution_scale = 2\n");
        settings.Set("swap_post_effect", "fxaa");
        settings.Save();
        Assert.Equal("fm4_frame_rate = 144\nunknown = 1\nresolution_scale = 2\nswap_post_effect = \"fxaa\"\n",
            File.ReadAllText(toml));
        Assert.Single(Directory.GetFiles(Path.Combine(temp.Path, "backups")));
    }

    [Fact]
    public void Frame_rate_ceiling_follows_the_display()
    {
        using var temp = new TempDir();
        var settings = new GraphicsSettings(Schema(), Path.Combine(temp.Path, "g.toml"), temp.Path);
        Assert.Equal(170, settings.FrameRateCeiling(170));
        Assert.Equal(240, settings.FrameRateCeiling(null));
        Assert.Equal(240, settings.FrameRateCeiling(360));
    }

    [Fact]
    public void A_malformed_file_shows_defaults_and_is_backed_up_before_saving()
    {
        using var temp = new TempDir();
        var original = "garbage\nresolution_scale = 2\n"u8.ToArray();
        var toml = temp.File("graphics.toml", original);
        var backups = Path.Combine(temp.Path, "backups");
        var settings = new GraphicsSettings(Schema(), toml, backups);
        Assert.True(settings.FileIsMalformed);
        Assert.Equal("2", settings.Value("resolution_scale"));
        Assert.Equal("none", settings.Value("swap_post_effect"));
        settings.Set("swap_post_effect", "fxaa");
        settings.Save();
        Assert.Equal(original, File.ReadAllBytes(Assert.Single(Directory.GetFiles(backups))));
        Assert.StartsWith("garbage\n", File.ReadAllText(toml));
    }

    [Fact]
    public void Restores_the_newest_backup()
    {
        using var temp = new TempDir();
        var toml = temp.File("graphics.toml", "resolution_scale = 1\n"u8.ToArray());
        var backups = Path.Combine(temp.Path, "backups");
        var first = new GraphicsSettings(Schema(), toml, backups);
        first.Set("resolution_scale", "2");
        first.Save();
        var second = new GraphicsSettings(Schema(), toml, backups);
        second.Set("swap_post_effect", "fxaa");
        second.Save();
        Assert.Equal(2, Directory.GetFiles(backups).Length);
        File.WriteAllText(toml, "resolution_scale = 1\nswap_post_effect = \"fxaa\"\n");
        var settings = new GraphicsSettings(Schema(), toml, backups);
        settings.Set("fm4_frame_rate", "144");
        Assert.True(settings.RestoreBackup());
        Assert.Equal("resolution_scale = 2\n", File.ReadAllText(toml));
        Assert.Equal("2", settings.Value("resolution_scale"));
        Assert.Equal("120", settings.Value("fm4_frame_rate"));
        settings.Save();
        Assert.Equal("resolution_scale = 2\n", File.ReadAllText(toml));
    }

    [Fact]
    public void Restoring_without_a_backup_changes_nothing()
    {
        using var temp = new TempDir();
        var toml = temp.File("graphics.toml", "resolution_scale = 2\n"u8.ToArray());
        Directory.CreateDirectory(Path.Combine(temp.Path, "backups"));
        var settings = new GraphicsSettings(Schema(), toml, Path.Combine(temp.Path, "backups"));
        Assert.False(settings.RestoreBackup());
        Assert.Equal("resolution_scale = 2\n", File.ReadAllText(toml));
    }

    [Fact]
    public void Reset_stages_every_default()
    {
        using var temp = new TempDir();
        var toml = temp.File("graphics.toml", "resolution_scale = 2\nswap_post_effect = \"fxaa\"\n"u8.ToArray());
        var settings = new GraphicsSettings(Schema(), toml, Path.Combine(temp.Path, "backups"));
        settings.ResetToDefaults();
        Assert.Equal("1", settings.Value("resolution_scale"));
        settings.Save();
        Assert.Equal("resolution_scale = 1\nswap_post_effect = \"none\"\nfm4_frame_rate = 120\nfh1_scaled_msaa_single_sample = true\n",
            File.ReadAllText(toml));
    }

    [Fact]
    public void Loads_the_schema_the_game_writes()
    {
        var real = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
            @"..\..\..\..\..\out\build\win-amd64-release\graphics-settings.json"));
        if (!File.Exists(real)) return;
        var schema = GraphicsSchema.Load(real);
        Assert.Equal(3, schema.Blocks.Count);
        Assert.Equal(17, schema.SettingRows.Count());
        Assert.Equal(["CONSOLE", "HIGH", "ULTRA", "EXPERIMENTAL"], schema.Presets.Select(p => p.Label));
        var labels = schema.Blocks.SelectMany(b => b.Rows).Select(r => r.Label).ToList();
        Assert.Contains("VSYNC", labels);
        Assert.Contains("CAR DIRT", labels);
        Assert.DoesNotContain("CAR SHADOWS", labels);
    }
}
