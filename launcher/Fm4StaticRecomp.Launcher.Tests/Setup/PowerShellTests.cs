using System.Diagnostics;
using Fm4StaticRecomp.Launcher.Core.Setup;

namespace Fm4StaticRecomp.Launcher.Tests.Setup;

// Changes the process environment, so nothing else runs beside it.
[CollectionDefinition(nameof(PowerShellTests), DisableParallelization = true)]
public sealed class PowerShellCollection;

[Collection(nameof(PowerShellTests))]
public class PowerShellTests
{
    [Fact]
    public async Task Runs_a_script_with_Windows_PowerShell_s_own_modules_when_started_from_PowerShell_7()
    {
        if (!OperatingSystem.IsWindows()) return;
        // A launcher started from a PowerShell 7 terminal inherits its module path, 7's folders first.
        string[] candidates =
        [
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PowerShell", "7", "Modules"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "scoop", "apps", "pwsh", "current", "Modules"),
        ];
        var modules = candidates.FirstOrDefault(c => Directory.Exists(Path.Combine(c, "Microsoft.PowerShell.Utility")));
        if (modules is null) return;
        using var temp = new TempDir();
        var script = Path.Combine(temp.Path, "hash.ps1");
        File.WriteAllText(script, "$ErrorActionPreference = 'Stop'\n(Get-FileHash -LiteralPath $PSCommandPath).Hash | Out-Null\n");
        var inherited = Environment.GetEnvironmentVariable("PSModulePath");
        Environment.SetEnvironmentVariable("PSModulePath", modules + Path.PathSeparator + inherited);
        try
        {
            var start = PowerShell.StartInfo(PowerShell.FileArguments(script));
            start.RedirectStandardError = true;
            using var process = Process.Start(start)!;
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            Assert.True(process.ExitCode == 0, error);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PSModulePath", inherited);
        }
    }
}
