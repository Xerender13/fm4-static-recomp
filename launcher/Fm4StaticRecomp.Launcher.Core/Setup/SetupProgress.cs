namespace Fm4StaticRecomp.Launcher.Core.Setup;

// Setup's stages up to the schema share one 0-100 scale; fonts and shaders each report their
// own. The shader warm-up takes the last tenth of the bar.
public static class SetupProgress
{
    public static double Overall(SetupEvent e) => e.Stage switch
    {
        "tools" or "sdk" or "codegen" or "build" or "schema" => e.Percent * 0.9,
        "fonts" => 90,
        "shaders" => 90 + e.Percent * 0.1,
        _ => -1,
    };
}
