using System.Text;
using Fm4StaticRecomp.Launcher.Core.Setup;

namespace Fm4StaticRecomp.Launcher.Tests.Setup;

public class ConsoleLinesTests
{
    private const string Text = @"Setup failed: C:\Тест 1\FM4";

    private static Encoding Oem()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(866);
    }

    [Fact]
    public void Decodes_utf8_and_the_console_code_page()
    {
        Assert.Equal(Text, ConsoleLines.Decode(Encoding.UTF8.GetBytes(Text + "\r"), Oem()));
        Assert.Equal(Text, ConsoleLines.Decode(Oem().GetBytes(Text), Oem()));
    }

    [Fact]
    public async Task Pumps_mixed_lines_and_a_last_line_without_a_newline()
    {
        var bytes = new List<byte>();
        bytes.AddRange(Encoding.UTF8.GetBytes(Text + "\r\n"));
        bytes.AddRange(Oem().GetBytes(Text + "\n"));
        bytes.AddRange("tail"u8.ToArray());
        var lines = new List<string>();
        await ConsoleLines.PumpAsync(new MemoryStream(bytes.ToArray()), Oem(), lines.Add, CancellationToken.None);
        Assert.Equal([Text, Text, "tail"], lines);
    }

    [Fact]
    public async Task Passes_on_each_redraw_of_a_progress_meter_as_it_comes()
    {
        // curl redraws its meter with carriage returns and ends it with one newline when done.
        var lines = new List<string>();
        await ConsoleLines.PumpAsync(new MemoryStream("  1 37.1M\r  6 37.1M\r100 37.1M\r\ndone\r\n"u8.ToArray()), Oem(), lines.Add,
            CancellationToken.None);
        Assert.Equal(["  1 37.1M", "  6 37.1M", "100 37.1M", "done"], lines);
    }
}
