// Adapted from Pinyon Shift's launcher (launcher/PinyonShift.Launcher.Desktop, BSD-3-Clause; see THIRD_PARTY_NOTICES.md).
using System.ComponentModel;
using Fm4StaticRecomp.Launcher.Core.Profiles;

namespace Fm4StaticRecomp.Launcher;

// Play and what the game's exit means.
public partial class MainWindow
{
    // Exit codes of a normal quit (the game's own Quit and the window's close button).
    private static readonly HashSet<int> NormalExitCodes = [0];

    // The resolution the next start uses, under the headline.
    private void UpdateSummary()
    {
        var scale = 1;
        try
        {
            if (_host?.Graphics() is { } graphics && int.TryParse(graphics.Value("resolution_scale"), out var value)) scale = value;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or System.Text.Json.JsonException or KeyNotFoundException) { }
        Subhead.Text = $"Vulkan · {scale}× ({1280 * scale} × {720 * scale}) · F6 opens settings in game";
    }

    private async Task PlayAsync(Profile? chosen = null)
    {
        if (_host is not { } host || (chosen ?? SelectedProfile) is not { } profile) return;
        StatusText.Text = "";
        ShowMode(Mode.Playing);
        var session = new GameSession(host);
        int exitCode;
        try
        {
            AppendLog($"Starting version {host.Settings.ActiveVersion} on profile {profile.Name}.");
            if (host.Settings.CommandLineOptions.Length > 0) AppendLog($"Command-line options: {host.Settings.CommandLineOptions}");
            exitCode = await session.RunAsync(profile);
        }
        catch (Exception error) when (error is Win32Exception or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            OpenSkippedWindow();
            ShowFailure("THE GAME DID NOT START", error.Message);
            return;
        }
        AppendLog($"The game exited with code 0x{exitCode:X8}.");
        ReloadProfiles();
        if (NormalExitCodes.Contains(exitCode))
        {
            if (_skip is not null)
            {
                ExitSkippedLauncher();
                return;
            }
            ShowMode(Mode.Ready);
            return;
        }
        OpenSkippedWindow();
        await OfferCrashReportAsync(session, exitCode);
    }
}
