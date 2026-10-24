namespace Fm4StaticRecomp.Launcher.Core.Updates;

public static class SelfReplace
{
    public const string AfterUpdateSwitch = "--after-update";

    // Windows lets a running executable be renamed but not overwritten.
    public static void Replace(string runningExe, string newExe)
    {
        var old = runningExe + ".old";
        if (File.Exists(old)) File.Delete(old);
        File.Move(runningExe, old);
        try
        {
            File.Move(newExe, runningExe);
        }
        catch
        {
            File.Move(old, runningExe);
            throw;
        }
    }

    public static void CleanUp(string runningExe)
    {
        var old = runningExe + ".old";
        for (var attempt = 0; attempt < 20 && File.Exists(old); attempt++)
        {
            try { File.Delete(old); }
            catch (IOException) { Thread.Sleep(250); }
            catch (UnauthorizedAccessException) { Thread.Sleep(250); }
        }
    }
}
