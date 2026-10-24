using Fm4StaticRecomp.Launcher.Core.Discs;

namespace Fm4StaticRecomp.Launcher.Tests.Discs;

public class GdfReaderTests
{
    private static readonly Dictionary<string, byte[]> Files = new()
    {
        ["default.xex"] = Enumerable.Range(0, 5000).Select(i => (byte)i).ToArray(),
        ["Media/Cars/abc.zip"] = [1, 2, 3],
        ["Media/empty.bin"] = [],
    };

    [Theory]
    [InlineData(0L)]
    [InlineData(0xFD90000L)]
    [InlineData(0x2080000L)]
    public void Lists_files_at_every_known_partition_offset(long partition)
    {
        var reader = GdfReader.Open(new MemoryStream(GdfImageBuilder.Build(Files, partition)));
        var files = reader.Entries.Where(e => !e.IsDirectory).Select(e => e.Path).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(["Media/Cars/abc.zip", "Media/empty.bin", "default.xex"], files);
        Assert.Equal(partition, reader.Base);
    }

    [Fact]
    public void Reads_file_content()
    {
        var reader = GdfReader.Open(new MemoryStream(GdfImageBuilder.Build(Files)));
        using var stream = reader.OpenFile(reader.Find("DEFAULT.XEX")!);
        var copy = new MemoryStream();
        stream.CopyTo(copy);
        Assert.Equal(Files["default.xex"], copy.ToArray());
    }

    [Fact]
    public void Rejects_names_that_differ_only_in_case()
    {
        var image = GdfImageBuilder.Build(new Dictionary<string, byte[]> { ["Media/a.bin"] = [1], ["Media/A.BIN"] = [2] });
        var error = Assert.Throws<InvalidDataException>(() => GdfReader.Open(new MemoryStream(image)));
        Assert.Contains("differ only in case", error.Message);
    }

    [Fact]
    public void Rejects_a_stream_without_a_volume_descriptor()
    {
        Assert.Throws<InvalidDataException>(() => GdfReader.Open(new MemoryStream(new byte[0x30000])));
    }

    [Theory]
    [InlineData("..")]
    [InlineData("a:b")]
    [InlineData("a\\b")]
    public void Rejects_unsafe_names(string name)
    {
        var image = GdfImageBuilder.Build(new Dictionary<string, byte[]> { [name] = [1] });
        Assert.Throws<InvalidDataException>(() => GdfReader.Open(new MemoryStream(image)));
    }

    [Fact]
    public void SafePath_keeps_paths_under_the_root()
    {
        using var temp = new TempDir();
        Assert.Equal(Path.Combine(temp.Path, "Media", "a.bin"), SafePath.Combine(temp.Path, "Media/a.bin"));
        Assert.Throws<InvalidDataException>(() => SafePath.Combine(temp.Path, "../x"));
    }
}
