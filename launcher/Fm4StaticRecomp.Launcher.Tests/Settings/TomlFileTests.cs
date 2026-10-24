using Fm4StaticRecomp.Launcher.Core.Settings;

namespace Fm4StaticRecomp.Launcher.Tests.Settings;

public class TomlFileTests
{
    [Fact]
    public void Reads_quoted_and_bare_values()
    {
        using var temp = new TempDir();
        var path = temp.File("g.toml", "a = 3\nb = \"fxaa\"\n# note\nc = true # trailing\n"u8.ToArray());
        var file = TomlFile.Load(path);
        Assert.Equal("3", file.Get("a"));
        Assert.Equal("fxaa", file.Get("b"));
        Assert.Equal("true", file.Get("c"));
        Assert.Null(file.Get("d"));
        Assert.False(file.IsMalformed);
    }

    [Fact]
    public void Keeps_unknown_keys_comments_and_order_when_saving()
    {
        using var temp = new TempDir();
        var path = temp.File("g.toml", "# head\nunknown_key = 7\nb = \"x\"\n"u8.ToArray());
        var file = TomlFile.Load(path);
        file.Set("b", "fxaa", quoted: true);
        file.Set("new_key", "2", quoted: false);
        file.Save(path);
        Assert.Equal("# head\nunknown_key = 7\nb = \"fxaa\"\nnew_key = 2\n", File.ReadAllText(path));
    }

    [Fact]
    public void Flags_lines_it_cannot_read_and_keeps_them()
    {
        using var temp = new TempDir();
        var path = temp.File("g.toml", "garbage line\na = 1\n"u8.ToArray());
        var file = TomlFile.Load(path);
        Assert.True(file.IsMalformed);
        file.Save(path);
        Assert.Equal("garbage line\na = 1\n", File.ReadAllText(path));
    }

    [Fact]
    public void A_missing_file_is_empty()
    {
        using var temp = new TempDir();
        Assert.Empty(TomlFile.Load(Path.Combine(temp.Path, "none.toml")).Keys);
    }

    [Fact]
    public void Keeps_quotes_and_backslashes_in_values()
    {
        using var temp = new TempDir();
        var path = Path.Combine(temp.Path, "p.toml");
        var file = TomlFile.Load(path);
        file.Set("name", "Bob \"Fast\" C:\\Games # 1", quoted: true);
        file.Save(path);
        Assert.Equal("name = \"Bob \\\"Fast\\\" C:\\\\Games # 1\"\n", File.ReadAllText(path));
        Assert.Equal("Bob \"Fast\" C:\\Games # 1", TomlFile.Load(path).Get("name"));
    }

    [Fact]
    public void Removes_a_key()
    {
        using var temp = new TempDir();
        var path = temp.File("g.toml", "a = 1\nb = 2\n"u8.ToArray());
        var file = TomlFile.Load(path);
        file.Remove("a");
        file.Remove("missing");
        file.Save(path);
        Assert.Equal("b = 2\n", File.ReadAllText(path));
    }

    [Fact]
    public void Reads_and_writes_cyrillic_values()
    {
        using var temp = new TempDir();
        var path = Path.Combine(temp.Path, "p.toml");
        var file = TomlFile.Load(path);
        file.Set("name", "Илья", quoted: true);
        file.Save(path);
        Assert.Equal("Илья", TomlFile.Load(path).Get("name"));
        Assert.Equal("name = \"Илья\"\n"u8.ToArray(), File.ReadAllBytes(path));
    }
}
