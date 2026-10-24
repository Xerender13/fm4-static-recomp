using System.Diagnostics;

namespace Fm4StaticRecomp.Launcher.Core.Setup;

public static class PowerShell
{
    // Windows PowerShell by its full path first, as Pinyon's launcher does: it ships with every
    // supported Windows, and a PATH that lost its folder made a bare "powershell.exe" fail.
    // PowerShell 7 serves when Windows PowerShell is gone.
    public static string Executable()
    {
        string[] candidates =
        [
            Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PowerShell", "7", "pwsh.exe"),
        ];
        return candidates.FirstOrDefault(File.Exists) ?? "powershell.exe";
    }

    public static IReadOnlyList<string> FileArguments(string script) =>
        ["-NoLogo", "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script];

    public static ProcessStartInfo StartInfo(IEnumerable<string> arguments)
    {
        var start = new ProcessStartInfo(Executable()) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        // Started from PowerShell 7, the launcher inherits a module path with 7's folders first;
        // Windows PowerShell would load their modules and lose Get-FileHash. Without the variable
        // each PowerShell builds its own.
        start.Environment.Remove("PSModulePath");
        return start;
    }
}
