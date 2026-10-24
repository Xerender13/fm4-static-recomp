using Fm4StaticRecomp.Launcher.Core.Discs;

namespace Fm4StaticRecomp.Launcher.Tests.Discs;

public class DiscInputTests
{
    [Fact]
    public void Detects_each_kind()
    {
        using var temp = new TempDir();
        var iso = temp.File("d1.iso", GdfImageBuilder.Build(TestDiscs.Pal()));
        var god = Path.Combine(temp.Path, "god");
        GodBuilder.Write(god, GdfImageBuilder.Build(TestDiscs.Pal()));
        foreach (var (name, data) in TestDiscs.Pal()) temp.File("extracted/" + name, data);
        temp.File("dlc/x/pack", GodBuilder.Header("LIVE", 2, 0x4D530910, 0, "Pack"));
        Assert.Equal(DiscInputKind.Iso, DiscInput.Detect(iso)!.Kind);
        var godInput = DiscInput.Detect(god)!;
        Assert.Equal((DiscInputKind.God, "61D62D9F"), (godInput.Kind, godInput.HeaderMediaId));
        Assert.Equal(DiscInputKind.ExtractedFolder, DiscInput.Detect(Path.Combine(temp.Path, "extracted"))!.Kind);
        Assert.Equal(DiscInputKind.PackageFolder, DiscInput.Detect(Path.Combine(temp.Path, "dlc"))!.Kind);
        Assert.Null(DiscInput.Detect(temp.File("note.txt", "hello"u8.ToArray())));
    }

    [Fact]
    public void A_folder_with_an_unreadable_subfolder_or_a_locked_file_is_no_disc()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var temp = new TempDir();
        temp.File("docs/note.txt", [1]);
        var locked = new DirectoryInfo(Path.Combine(temp.Path, "docs", "private"));
        locked.Create();
        var rule = new System.Security.AccessControl.FileSystemAccessRule(
            System.Security.Principal.WindowsIdentity.GetCurrent().User!, System.Security.AccessControl.FileSystemRights.ListDirectory,
            System.Security.AccessControl.AccessControlType.Deny);
        var security = locked.GetAccessControl();
        security.AddAccessRule(rule);
        locked.SetAccessControl(security);
        try
        {
            Assert.Null(DiscInput.Detect(Path.Combine(temp.Path, "docs")));
            var iso = temp.File("busy.iso", GdfImageBuilder.Build(TestDiscs.Pal()));
            using (new FileStream(iso, FileMode.Open, FileAccess.Read, FileShare.None))
                Assert.Null(DiscInput.Detect(iso));
        }
        finally
        {
            security.RemoveAccessRule(rule);
            locked.SetAccessControl(security);
        }
    }

    [Fact]
    public void Lists_files_that_are_links_but_not_linked_folders()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var temp = new TempDir();
        foreach (var (name, data) in TestDiscs.Pal()) temp.File("source/" + name, data);
        var extracted = Path.Combine(temp.Path, "extracted");
        Directory.CreateDirectory(extracted);
        try { File.CreateSymbolicLink(Path.Combine(extracted, "default.xex"), Path.Combine(temp.Path, "source", "default.xex")); }
        catch (IOException) { return; }
        catch (UnauthorizedAccessException) { return; }
        new Core.Platform.WindowsPlatform().CreateDirectoryLink(Path.Combine(extracted, "Media"), Path.Combine(temp.Path, "source", "Media"));
        using var files = DiscInput.Detect(extracted)!.OpenFiles();
        Assert.Equal(["default.xex"], files.Files.Select(f => f.Path));
    }

    [Fact]
    public void Lists_an_extracted_folder_with_slashes()
    {
        using var temp = new TempDir();
        foreach (var (name, data) in TestDiscs.Pal()) temp.File("extracted/" + name, data);
        using var files = DiscInput.Detect(Path.Combine(temp.Path, "extracted"))!.OpenFiles();
        Assert.Equal(["$SystemUpdate/su.bin", "Media/a.bin", "Media/b.bin", "default.xex"],
            files.Files.Select(f => f.Path).Order(StringComparer.Ordinal));
        using var stream = files.Open(files.Find("MEDIA/A.BIN")!);
        Assert.Equal(2, stream.Length);
    }
}
