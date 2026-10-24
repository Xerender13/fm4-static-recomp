using Fm4StaticRecomp.Launcher.Core.Discs;

namespace Fm4StaticRecomp.Launcher.Tests.Discs;

public class XexInfoTests
{
    [Fact]
    public void Reads_the_media_id() =>
        Assert.Equal("61D62D9F", XexInfo.MediaId(new MemoryStream(TestDiscs.Xex(0x61D62D9F))));

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Returns_null_for_other_files(int kind) =>
        Assert.Null(XexInfo.MediaId(new MemoryStream(kind == 0 ? new byte[64] : "XEX2"u8.ToArray())));

    [Fact]
    public void Returns_null_when_the_offset_lies_past_the_end()
    {
        var xex = TestDiscs.Xex(0x61D62D9F);
        xex[0x1F] = 0xF0;
        Assert.Null(XexInfo.MediaId(new MemoryStream(xex)));
    }
}
