using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Fm4StaticRecomp.Launcher.Core.Settings;

namespace Fm4StaticRecomp.Launcher.Core.Setup;

// The licences of the Microsoft files the build downloads, as tools\microsoft-licences.ps1
// reads them from Microsoft. The player's acceptance holds until either URL changes.
public sealed record MicrosoftLicences(
    [property: JsonPropertyName("build_tools")] string BuildTools,
    [property: JsonPropertyName("windows_sdk")] string WindowsSdk)
{
    public string Key => BuildTools + " " + WindowsSdk;

    public bool AcceptedIn(LauncherSettings settings) => settings.AcceptedMicrosoftLicence == Key;

    public static MicrosoftLicences Parse(string json)
    {
        var licences = JsonSerializer.Deserialize<MicrosoftLicences>(json);
        // The links open in the player's browser.
        if (licences is null || !IsHttps(licences.BuildTools) || !IsHttps(licences.WindowsSdk))
            throw new InvalidDataException($"Not a licence line: {json}");
        return licences;
    }

    private static bool IsHttps(string? link) =>
        Uri.TryCreate(link, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;

    public static async Task<MicrosoftLicences> ReadAsync(string script, CancellationToken cancellation)
    {
        var start = PowerShell.StartInfo(PowerShell.FileArguments(script));
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(cancellation);
        var error = process.StandardError.ReadToEndAsync(cancellation);
        try
        {
            await process.WaitForExitAsync(cancellation);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"The Microsoft licences could not be read: {(await error).Trim()}");
        var line = (await output).Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.StartsWith('{'))
                   ?? throw new InvalidOperationException("The licence script printed no licences.");
        return Parse(line);
    }
}
