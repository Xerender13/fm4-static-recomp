using Fm4StaticRecomp.Launcher.Core.Content;

namespace Fm4StaticRecomp.Launcher.Tests.Content;

public class ContentLogTests
{
    [Theory]
    [InlineData("[2026-10-16 10:53:12.855] [info] [core] [t22180] Content: installed 4d53091000000001", "Installed")]
    [InlineData("i> Content: skipped 4d53091000000002 (installed)", "Skipped")]
    [InlineData("[error] Content: installing C:\\x\\pack failed: 80070005", "Failed")]
    [InlineData("Content: installing C:\\dlc\\pack", "Installing")]
    public void Recognises_each_line(string line, string kind) =>
        Assert.Equal(kind, ContentLog.Parse(line)!.GetType().Name);

    [Fact]
    public void Reads_the_summary()
    {
        var finished = Assert.IsType<ContentEvent.Finished>(
            ContentLog.Parse("[info] Content: install finished, 2 installed, 14 skipped, 1 failed"));
        Assert.Equal(new ContentEvent.Finished(2, 14, 1), finished);
    }

    [Fact]
    public void Ignores_other_lines() => Assert.Null(ContentLog.Parse("Graphics settings: x = 1"));
}
