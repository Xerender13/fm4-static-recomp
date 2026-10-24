using Fm4StaticRecomp.Launcher.Core.Discs;

namespace Fm4StaticRecomp.Launcher.Tests.Discs;

public class GodTests
{
    private static readonly Dictionary<string, byte[]> Files = new()
    {
        ["default.xex"] = Enumerable.Range(0, 300_000).Select(i => (byte)(i * 7)).ToArray(),
        ["Media/a.bin"] = [9, 9, 9],
    };

    [Fact]
    public void Maps_data_blocks_across_hash_blocks_and_files()
    {
        using var temp = new TempDir();
        var data = Enumerable.Range(0, 3 * 204 * 0x1000 + 123).Select(i => (byte)(i % 251)).ToArray();
        GodBuilder.Write(temp.Path, data, maxSubpartsPerFile: 2);
        var container = GodContainer.Find(temp.Path, subpartsPerFile: 2)!;
        using var stream = container.OpenData();
        Assert.True(stream.Length >= data.Length);
        var copy = new byte[data.Length];
        stream.ReadExactly(copy);
        Assert.Equal(data, copy);
    }

    [Fact]
    public void Closes_the_data_files_when_no_file_system_is_found()
    {
        using var temp = new TempDir();
        GodBuilder.Write(temp.Path, new byte[3 * 204 * 0x1000], maxSubpartsPerFile: 2);
        var container = GodContainer.Find(temp.Path, subpartsPerFile: 2)!;
        Assert.Throws<InvalidDataException>(() => container.OpenGdf());
        foreach (var file in container.DataFiles) File.Delete(file);
    }

    [Fact]
    public void Closes_the_data_files_already_open_when_a_later_one_cannot_open()
    {
        using var temp = new TempDir();
        GodBuilder.Write(temp.Path, new byte[3 * 204 * 0x1000], maxSubpartsPerFile: 2);
        var container = GodContainer.Find(temp.Path, subpartsPerFile: 2)!;
        using (new FileStream(container.DataFiles[^1], FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.Throws<IOException>(() => container.OpenData());
        File.Delete(container.DataFiles[0]);
    }

    [Fact]
    public void Finds_the_file_system_when_data_starts_at_the_partition()
    {
        using var temp = new TempDir();
        GodBuilder.Write(temp.Path, GdfImageBuilder.Build(Files));
        using var reader = GodContainer.Find(temp.Path)!.OpenGdf();
        Assert.NotNull(reader.Find("default.xex"));
        Assert.Equal(0, reader.Base);
    }

    [Fact]
    public void Finds_the_file_system_when_data_starts_at_the_volume_descriptor()
    {
        using var temp = new TempDir();
        var image = GdfImageBuilder.Build(Files);
        GodBuilder.Write(temp.Path, image[0x10000..]);
        using var reader = GodContainer.Find(temp.Path)!.OpenGdf();
        Assert.Equal(-0x10000, reader.Base);
        using var file = reader.OpenFile(reader.Find("default.xex")!);
        var copy = new MemoryStream();
        file.CopyTo(copy);
        Assert.Equal(Files["default.xex"], copy.ToArray());
    }

    [Fact]
    public void Reports_title_and_media_from_the_header()
    {
        using var temp = new TempDir();
        GodBuilder.Write(temp.Path, GdfImageBuilder.Build(Files));
        var header = GodContainer.Find(temp.Path)!.Header;
        Assert.Equal(0x4D530910u, header.TitleId);
        Assert.Equal(0x61D62D9Fu, header.MediaId);
    }

    [Fact]
    public void Returns_null_for_a_folder_without_a_container()
    {
        using var temp = new TempDir();
        temp.File("x.bin", [1, 2, 3]);
        Assert.Null(GodContainer.Find(temp.Path));
    }
}
