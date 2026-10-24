using Fm4StaticRecomp.Launcher.Core.Discs;

namespace Fm4StaticRecomp.Launcher.Tests.Discs;

public class PackageHeaderTests
{
    [Fact]
    public void Reads_a_marketplace_package_header()
    {
        var bytes = GodBuilder.Header("LIVE", 2, 0x4D530910, 0, "Porsche Expansion");
        var header = PackageHeader.TryRead(new MemoryStream(bytes))!;
        Assert.Equal("LIVE", header.Magic);
        Assert.Equal(PackageHeader.MarketplaceContent, header.ContentType);
        Assert.Equal(PackageHeader.Fm4TitleId, header.TitleId);
        Assert.Equal("Porsche Expansion", header.DisplayName);
    }

    [Theory]
    [InlineData("LIVE", 0x00000002u, 0x4D530910u, true)]
    [InlineData("PIRS", 0x00000002u, 0x4D530910u, true)]
    [InlineData("LIVE", 0x000B0000u, 0x4D530910u, false)]
    [InlineData("LIVE", 0x00000002u, 0x4D5307E6u, false)]
    [InlineData("CON ", 0x00000002u, 0x4D530910u, false)]
    public void Tells_fm4_dlc_from_title_updates_and_other_titles(string magic, uint contentType, uint titleId, bool dlc) =>
        Assert.Equal(dlc, PackageHeader.TryRead(new MemoryStream(GodBuilder.Header(magic, contentType, titleId, 0, "x")))!.IsFm4Dlc);

    [Fact]
    public void Returns_null_for_other_files()
    {
        Assert.Null(PackageHeader.TryRead(new MemoryStream(new byte[0x1000])));
        Assert.Null(PackageHeader.TryRead(new MemoryStream("LIVE"u8.ToArray())));
    }
}
