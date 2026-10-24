using System.Text;
using System.Text.RegularExpressions;

namespace Fm4StaticRecomp.Launcher.Core.Settings;

// The flat key = value files the game and launcher share. Lines it does not change are
// written back as they were, so keys of a newer or older version survive.
public sealed partial class TomlFile
{
    private readonly List<string> _lines;

    private TomlFile(List<string> lines) => _lines = lines;

    public bool IsMalformed => _lines.Any(l => !IsBlankOrComment(l) && !KeyValue().IsMatch(l));

    public IReadOnlyList<string> Keys =>
        _lines.Select(l => KeyValue().Match(l)).Where(m => m.Success).Select(m => m.Groups[1].Value).ToList();

    public static TomlFile Load(string path) =>
        new(File.Exists(path)
            ? File.ReadAllText(path).Replace("\r\n", "\n").Split('\n').SkipLastIfEmpty().ToList()
            : []);

    public string? Get(string key)
    {
        foreach (var line in _lines)
        {
            var match = KeyValue().Match(line);
            if (match.Success && match.Groups[1].Value == key) return ParseValue(match.Groups[2].Value);
        }
        return null;
    }

    public void Set(string key, string value, bool quoted)
    {
        var text = $"{key} = {(quoted ? Quote(value) : value)}";
        for (var i = 0; i < _lines.Count; i++)
        {
            var match = KeyValue().Match(_lines[i]);
            if (match.Success && match.Groups[1].Value == key) { _lines[i] = text; return; }
        }
        _lines.Add(text);
    }

    public void Remove(string key) => _lines.RemoveAll(l => KeyValue().Match(l) is { Success: true } m && m.Groups[1].Value == key);

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, string.Concat(_lines.Select(l => l + "\n")), new UTF8Encoding(false));
        File.Move(temporary, path, overwrite: true);
    }

    private static bool IsBlankOrComment(string line) => line.Trim() is "" || line.TrimStart().StartsWith('#');

    // Escaped as TOML escapes them (profile names, paths). The game's own writer drops " and \
    // instead (host_config.cpp); the values it writes never hold either.
    private static string Quote(string value) =>
        "\"" + string.Concat(value.Where(c => c is not '\r' and not '\n').Select(c => c is '"' or '\\' ? "\\" + c : c.ToString())) + "\"";

    private static string ParseValue(string raw)
    {
        raw = raw.Trim();
        if (raw.StartsWith('"'))
        {
            var text = new StringBuilder();
            for (var i = 1; i < raw.Length && raw[i] != '"'; i++)
                text.Append(raw[i] == '\\' && i + 1 < raw.Length ? raw[++i] : raw[i]);
            return text.ToString();
        }
        var comment = raw.IndexOf('#');
        return (comment >= 0 ? raw[..comment] : raw).Trim();
    }

    [GeneratedRegex(@"^\s*([A-Za-z0-9_]+)\s*=\s*(.*?)\s*$")]
    private static partial Regex KeyValue();
}

internal static class LineExtensions
{
    public static IEnumerable<string> SkipLastIfEmpty(this string[] lines) =>
        lines.Length > 0 && lines[^1].Length == 0 ? lines[..^1] : lines;
}
