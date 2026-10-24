namespace Fm4StaticRecomp.Launcher.Core.Fonts;

// The disc's heading fonts as the built game exports them (--fm4_export_ui_fonts):
// Swiss 721 Bold Condensed and Swiss 721 Medium. Never shipped with the launcher.
public sealed record UiFonts(VectorFont Heading, VectorFont Medium)
{
    public const string HeadingFile = "a_vector_aa.dt";
    public const string MediumFile = "b_vector_aa.dt";

    public static UiFonts? Load(string folder)
    {
        var heading = Read(Path.Combine(folder, HeadingFile));
        var medium = Read(Path.Combine(folder, MediumFile));
        return heading is null || medium is null ? null : new UiFonts(heading, medium);
    }

    private static VectorFont? Read(string path)
    {
        try { return File.Exists(path) ? VectorFont.Parse(File.ReadAllBytes(path)) : null; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }
}
