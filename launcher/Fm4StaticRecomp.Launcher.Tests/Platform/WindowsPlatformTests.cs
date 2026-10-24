using Fm4StaticRecomp.Launcher.Core.Platform;

namespace Fm4StaticRecomp.Launcher.Tests.Platform;

public class WindowsPlatformTests
{
    [Theory]
    [InlineData("profile", "content")]
    [InlineData("profile x", "content x")]
    [InlineData("профиль x", "content x")]
    [InlineData("a&b", "c^d")]
    [InlineData("100%", "%PATH%")]
    public void Creates_a_junction_that_reads_through(string profile, string content)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var temp = new TempDir();
        var target = Path.Combine(temp.Path, content);
        temp.File(content + "/a.txt", "x"u8.ToArray());
        var link = Path.Combine(temp.Path, profile, "0000000000000000");
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        var platform = new WindowsPlatform();
        platform.CreateDirectoryLink(link, target);
        Assert.True(platform.IsDirectoryLink(link));
        Assert.True(File.Exists(Path.Combine(link, "a.txt")));
        Assert.False(platform.IsDirectoryLink(target));
    }

    [Fact]
    public void Decodes_console_output_in_the_oem_code_page()
    {
        if (!OperatingSystem.IsWindows()) return;
        // 65001 on a PC with Windows' "Use Unicode UTF-8 for worldwide language support" option on.
        var oem = (string)Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Nls\CodePage", "OEMCP", null)!;
        Assert.Equal(int.Parse(oem), new WindowsPlatform().ConsoleEncoding.CodePage);
    }
}
