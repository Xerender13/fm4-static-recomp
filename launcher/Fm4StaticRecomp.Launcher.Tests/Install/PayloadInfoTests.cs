using System.IO.Compression;
using Fm4StaticRecomp.Launcher.Core.Install;

namespace Fm4StaticRecomp.Launcher.Tests.Install;

public class PayloadInfoTests
{
    private static void WritePayload(TempDir temp, string version)
    {
        using var zip = ZipFile.Open(Path.Combine(temp.Path, PayloadInfo.FileName), ZipArchiveMode.Create);
        using var writer = new StreamWriter(zip.CreateEntry("config/release.json").Open());
        writer.Write($$"""{"schema_version":1,"version":"{{version}}","repository":"https://github.com/someone/fm4-static-recomp"}""");
    }

    [Fact]
    public void Reads_version_and_repository()
    {
        using var temp = new TempDir();
        WritePayload(temp, "0.2.0");
        var payload = PayloadInfo.Read(temp.Path, "0.2.0");
        Assert.Equal("0.2.0", payload.Version);
        Assert.Equal("someone/fm4-static-recomp", payload.OwnerRepo);
    }

    [Fact]
    public void Refuses_a_payload_of_another_version()
    {
        using var temp = new TempDir();
        WritePayload(temp, "0.1.0");
        var error = Assert.Throws<InvalidDataException>(() => PayloadInfo.Read(temp.Path, "0.2.0"));
        Assert.Contains("0.1.0", error.Message);
    }

    [Fact]
    public void Explains_a_missing_payload()
    {
        using var temp = new TempDir();
        Assert.Throws<FileNotFoundException>(() => PayloadInfo.Read(temp.Path, "0.2.0"));
    }

    [Fact]
    public void Extracts_one_entry()
    {
        using var temp = new TempDir();
        WritePayload(temp, "0.2.0");
        var path = PayloadInfo.Read(temp.Path, "0.2.0").ExtractEntry("config/release.json", Path.Combine(temp.Path, "out"));
        Assert.Equal(Path.Combine(temp.Path, "out", "config", "release.json"), path);
        Assert.Contains("\"0.2.0\"", File.ReadAllText(path));
        Assert.Throws<FileNotFoundException>(() =>
            PayloadInfo.Read(temp.Path, "0.2.0").ExtractEntry("config/none.json", Path.Combine(temp.Path, "out")));
    }
}
