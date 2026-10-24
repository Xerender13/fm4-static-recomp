using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fm4StaticRecomp.Launcher.Core.Settings;

public sealed record SchemaChoice([property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("value")] string Value);

public sealed record SchemaRow(
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("key")] string? Key,
    [property: JsonPropertyName("string")] bool String,
    [property: JsonPropertyName("choices")] IReadOnlyList<SchemaChoice>? Choices,
    [property: JsonPropertyName("low")] int Low,
    [property: JsonPropertyName("high")] int High,
    [property: JsonPropertyName("default")] string? Default,
    [property: JsonPropertyName("description")] string Description)
{
    public bool Accepts(string value) => Kind switch
    {
        "range" => int.TryParse(value, out var n) && n.ToString() == value && n >= Low && n <= High,
        "choice" => Choices!.Any(c => c.Value == value),
        _ => false,
    };
}

public sealed record SchemaBlock([property: JsonPropertyName("rows")] IReadOnlyList<SchemaRow> Rows);

public sealed record SchemaPreset([property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("values")] IReadOnlyDictionary<string, string> Values);

public sealed record GraphicsSchema(
    [property: JsonPropertyName("schema_version")] int SchemaVersion,
    [property: JsonPropertyName("blocks")] IReadOnlyList<SchemaBlock> Blocks,
    [property: JsonPropertyName("presets")] IReadOnlyList<SchemaPreset> Presets)
{
    public IEnumerable<SchemaRow> SettingRows => Blocks.SelectMany(b => b.Rows).Where(r => r.Key is not null);

    public static GraphicsSchema Load(string path)
    {
        var schema = JsonSerializer.Deserialize<GraphicsSchema>(File.ReadAllBytes(path))
                     ?? throw new InvalidDataException($"{path} is empty.");
        if (schema.SchemaVersion != 1) throw new InvalidDataException($"{path}: unknown schema version {schema.SchemaVersion}.");
        return schema;
    }
}
