namespace Fm4StaticRecomp.Launcher.Core.Settings;

public sealed class GraphicsSettings(GraphicsSchema schema, string tomlPath, string backupsFolder)
{
    private readonly Dictionary<string, string> _changed = [];
    private TomlFile _file = TomlFile.Load(tomlPath);
    private bool _backedUp;

    public GraphicsSchema Schema => schema;
    public bool FileIsMalformed => _file.IsMalformed;

    public string Value(string key)
    {
        if (_changed.TryGetValue(key, out var changed)) return changed;
        var row = Row(key);
        var stored = _file.Get(key);
        return stored is not null && row.Accepts(stored) ? stored : row.Default ?? "";
    }

    public void Set(string key, string value)
    {
        if (!Row(key).Accepts(value)) throw new ArgumentException($"{key} does not accept {value}.");
        _changed[key] = value;
    }

    public string? CurrentPreset() =>
        schema.Presets.FirstOrDefault(p => p.Values.All(v => Value(v.Key) == v.Value))?.Label;

    public void ApplyPreset(string label)
    {
        foreach (var (key, value) in schema.Presets.Single(p => p.Label == label).Values) Set(key, value);
    }

    public int FrameRateCeiling(int? displayHz)
    {
        var row = Row("fm4_frame_rate");
        return displayHz is > 0 ? Math.Clamp(displayHz.Value, row.Low, row.High) : row.High;
    }

    // The game may have written the file since it was read: only this session's changes are
    // applied on top of what is there now.
    public void Save()
    {
        if (_changed.Count == 0) return;
        if (!_backedUp && File.Exists(tomlPath))
        {
            Directory.CreateDirectory(backupsFolder);
            File.Copy(tomlPath, NewBackupPath(), overwrite: false);
            _backedUp = true;
        }
        _file = TomlFile.Load(tomlPath);
        foreach (var (key, value) in _changed) _file.Set(key, value, Row(key).String);
        _file.Save(tomlPath);
        _changed.Clear();
    }

    public void ResetToDefaults()
    {
        foreach (var row in schema.SettingRows.Where(r => r.Default is not null)) _changed[row.Key!] = row.Default!;
    }

    public bool RestoreBackup()
    {
        var newest = Directory.Exists(backupsFolder)
            ? Directory.GetFiles(backupsFolder, "graphics-*.toml").Max(StringComparer.Ordinal)
            : null;
        if (newest is null) return false;
        File.Copy(newest, tomlPath, overwrite: true);
        _changed.Clear();
        _file = TomlFile.Load(tomlPath);
        return true;
    }

    // The game's own name for a backup (src\config\host_config.cpp), so the newest of both
    // writers sorts last.
    private string NewBackupPath()
    {
        var stamp = DateTime.UtcNow;
        string path;
        while (File.Exists(path = Path.Combine(backupsFolder, $"graphics-{stamp:yyyyMMdd'T'HHmmssfff'Z'}.toml")))
            stamp = stamp.AddMilliseconds(1);
        return path;
    }

    private SchemaRow Row(string key) =>
        schema.SettingRows.FirstOrDefault(r => r.Key == key) ?? throw new KeyNotFoundException(key);
}
