using System.Text.Json;
using System.Text.Json.Serialization;

namespace Fm4StaticRecomp.Launcher.Core.Discs;

public sealed record ManifestFile(
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("size")] long Size,
    [property: JsonPropertyName("sha256")] string Sha256,
    [property: JsonPropertyName("optional")] bool Optional = false);

public sealed record Disc1Release(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("media_id")] string MediaId,
    [property: JsonPropertyName("files")] IReadOnlyList<ManifestFile> Files);

public sealed record Disc1Manifest(
    [property: JsonPropertyName("releases")] IReadOnlyList<Disc1Release> Releases,
    [property: JsonPropertyName("files")] IReadOnlyList<ManifestFile> Files);

public sealed record ManifestPackage(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("size")] long Size,
    [property: JsonPropertyName("sha256")] string Sha256);

public sealed record Disc2Manifest([property: JsonPropertyName("packages")] IReadOnlyList<ManifestPackage> Packages);

public sealed record DiscManifest(
    [property: JsonPropertyName("schema_version")] int SchemaVersion,
    [property: JsonPropertyName("title_id")] string TitleId,
    [property: JsonPropertyName("disc1")] Disc1Manifest Disc1,
    [property: JsonPropertyName("disc2")] Disc2Manifest Disc2)
{
    public const int SupportedSchema = 2;

    public static DiscManifest Load(string path)
    {
        DiscManifest? manifest;
        try { manifest = JsonSerializer.Deserialize<DiscManifest>(File.ReadAllBytes(path)); }
        catch (JsonException ex) { throw new InvalidDataException($"{path} is not a disc manifest: {ex.Message}", ex); }
        if (manifest is null) throw new InvalidDataException($"{path} is empty.");
        if (manifest.SchemaVersion != SupportedSchema)
            throw new InvalidDataException($"{path} has schema {manifest.SchemaVersion}; this launcher reads schema {SupportedSchema}.");
        if (manifest.Disc1?.Releases is null || manifest.Disc1.Files is null || manifest.Disc2?.Packages is null)
            throw new InvalidDataException($"{path} lacks the Disc 1 or Disc 2 lists.");
        return manifest;
    }

    public Disc1Release? Release(string mediaId) =>
        Disc1.Releases.FirstOrDefault(r => string.Equals(r.MediaId, mediaId, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<ManifestFile> Disc1Files(Disc1Release release) => [.. Disc1.Files, .. release.Files];
}
