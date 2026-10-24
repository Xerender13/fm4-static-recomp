using System.Net;
using System.Security.Cryptography;
using Fm4StaticRecomp.Launcher.Core.Install;
using Fm4StaticRecomp.Launcher.Core.Updates;

namespace Fm4StaticRecomp.Launcher.Tests.Updates;

public class UpdateCoordinatorTests
{
    private static readonly byte[] Exe = "new launcher"u8.ToArray();
    private static readonly byte[] Source = "new source"u8.ToArray();

    private static FakeHandler Server(string version, bool corrupt = false, bool prerelease = false)
    {
        var handler = new FakeHandler();
        handler.Routes["https://api.test/repos/o/r/releases?per_page=30"] = _ => FakeHandler.Json($$"""
            [{"tag_name":"v{{version}}","body":"notes","draft":false,"prerelease":{{(prerelease ? "true" : "false")}},"assets":[
              {"name":"FM4StaticRecompLauncher.exe","browser_download_url":"https://dl.test/exe"},
              {"name":"fm4-static-recomp-source.zip","browser_download_url":"https://dl.test/zip"},
              {"name":"SHA256SUMS.txt","browser_download_url":"https://dl.test/sums"}]}]
            """);
        handler.Routes["https://dl.test/exe"] = _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Exe) };
        handler.Routes["https://dl.test/zip"] = _ => new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new ByteArrayContent(corrupt ? [0] : Source) };
        handler.Routes["https://dl.test/sums"] = _ => FakeHandler.Json(
            $"{Convert.ToHexString(SHA256.HashData(Exe))}  FM4StaticRecompLauncher.exe\n{Convert.ToHexString(SHA256.HashData(Source))}  fm4-static-recomp-source.zip\n");
        return handler;
    }

    private static UpdateCoordinator Make(FakeHandler handler, TempDir temp, string current) =>
        new(new ReleaseClient(new HttpClient(handler), "o/r", "https://api.test"), new Downloader(new HttpClient(handler)),
            new InstallLayout(temp.Path), Version.Parse(current));

    [Fact]
    public async Task Offers_only_a_newer_release()
    {
        using var temp = new TempDir();
        Assert.NotNull(await Make(Server("0.2.0"), temp, "0.1.0").CheckAsync(CancellationToken.None));
        Assert.Null(await Make(Server("0.2.0"), temp, "0.2.0").CheckAsync(CancellationToken.None));
        Assert.Null(await Make(Server("0.1.0"), temp, "0.2.0").CheckAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Offers_a_newer_prerelease()
    {
        using var temp = new TempDir();
        var release = await Make(Server("0.3.0", prerelease: true), temp, "0.2.0").CheckAsync(CancellationToken.None);
        Assert.Equal((new Version(0, 3, 0), true), (release!.Version, release.Prerelease));
    }

    [Fact]
    public async Task Downloads_verifies_and_installs_beside_the_launcher()
    {
        using var temp = new TempDir();
        var coordinator = Make(Server("0.2.0"), temp, "0.1.0");
        var release = (await coordinator.CheckAsync(CancellationToken.None))!;
        var staged = await coordinator.DownloadAsync(release, null, CancellationToken.None);
        Assert.Equal(Path.Combine(temp.Path, "downloads", "0.2.0", "FM4StaticRecompLauncher.exe"), staged.LauncherExe);
        var launcherDir = Path.Combine(temp.Path, "launcher");
        var exe = temp.File("launcher/FM4StaticRecompLauncher.exe", "old"u8.ToArray());
        temp.File("launcher/fm4-static-recomp-source.zip", "old source"u8.ToArray());
        UpdateCoordinator.Install(staged, exe, launcherDir);
        Assert.Equal(Exe, File.ReadAllBytes(exe));
        Assert.Equal("old"u8.ToArray(), File.ReadAllBytes(exe + ".old"));
        Assert.Equal(Source, File.ReadAllBytes(Path.Combine(launcherDir, "fm4-static-recomp-source.zip")));
    }

    [Fact]
    public async Task A_corrupted_download_is_refused()
    {
        using var temp = new TempDir();
        var coordinator = Make(Server("0.2.0", corrupt: true), temp, "0.1.0");
        var release = (await coordinator.CheckAsync(CancellationToken.None))!;
        await Assert.ThrowsAsync<ChecksumMismatchException>(() => coordinator.DownloadAsync(release, null, CancellationToken.None));
        Assert.False(File.Exists(Path.Combine(temp.Path, "downloads", "0.2.0", "fm4-static-recomp-source.zip")));
    }

    private static (string Exe, string Zip, string Directory) Installed(TempDir temp)
    {
        var launcherDir = Path.Combine(temp.Path, "launcher");
        var exe = temp.File("launcher/FM4StaticRecompLauncher.exe", "old"u8.ToArray());
        var zip = temp.File("launcher/fm4-static-recomp-source.zip", "old source"u8.ToArray());
        var staged = new StagedUpdate(new Version(0, 2, 0), temp.File("downloads/FM4StaticRecompLauncher.exe", Exe),
            temp.File("downloads/fm4-static-recomp-source.zip", Source));
        UpdateCoordinator.Install(staged, exe, launcherDir);
        return (exe, zip, launcherDir);
    }

    [Fact]
    public void A_new_launcher_that_cannot_start_is_undone()
    {
        using var temp = new TempDir();
        var (exe, zip, launcherDir) = Installed(temp);
        UpdateCoordinator.Undo(exe, launcherDir);
        Assert.Equal("old"u8.ToArray(), File.ReadAllBytes(exe));
        Assert.Equal("old source"u8.ToArray(), File.ReadAllBytes(zip));
        Assert.False(File.Exists(exe + ".old"));
        Assert.False(File.Exists(zip + ".old"));
    }

    [Fact]
    public void The_restarted_launcher_removes_both_old_files()
    {
        using var temp = new TempDir();
        var (exe, zip, launcherDir) = Installed(temp);
        var downloaded = UpdateCoordinator.DownloadFolder(new InstallLayout(temp.Path), new Version(0, 2, 0));
        temp.File("downloads/0.2.0/fm4-static-recomp-source.zip", Source);
        UpdateCoordinator.CleanUp(exe, launcherDir, downloaded);
        Assert.False(Directory.Exists(downloaded));
        Assert.Equal(Exe, File.ReadAllBytes(exe));
        Assert.Equal(Source, File.ReadAllBytes(zip));
        Assert.False(File.Exists(exe + ".old"));
        Assert.False(File.Exists(zip + ".old"));
    }

    [Fact]
    public void An_interrupted_install_puts_both_old_files_back()
    {
        using var temp = new TempDir();
        var launcherDir = Path.Combine(temp.Path, "launcher");
        var exe = temp.File("launcher/FM4StaticRecompLauncher.exe", "old"u8.ToArray());
        var zip = temp.File("launcher/fm4-static-recomp-source.zip", "old source"u8.ToArray());
        var source = temp.File("downloads/fm4-static-recomp-source.zip", Source);
        var staged = new StagedUpdate(new Version(0, 2, 0), Path.Combine(temp.Path, "downloads", "missing.exe"), source);
        Assert.ThrowsAny<IOException>(() => UpdateCoordinator.Install(staged, exe, launcherDir));
        Assert.Equal("old"u8.ToArray(), File.ReadAllBytes(exe));
        Assert.Equal("old source"u8.ToArray(), File.ReadAllBytes(zip));
        Assert.False(File.Exists(zip + ".old"));
    }
}
