// Adapted from Pinyon Shift's launcher (launcher/PinyonShift.Launcher.Desktop, BSD-3-Clause; see THIRD_PARTY_NOTICES.md).
using System.Diagnostics;
using Fm4StaticRecomp.Launcher.Core.Content;
using Fm4StaticRecomp.Launcher.Core.Discs;
using Fm4StaticRecomp.Launcher.Core.Fonts;
using Fm4StaticRecomp.Launcher.Core.Install;
using Fm4StaticRecomp.Launcher.Core.Platform;
using Fm4StaticRecomp.Launcher.Core.Profiles;
using Fm4StaticRecomp.Launcher.Core.Settings;
using Fm4StaticRecomp.Launcher.Core.Setup;
using Fm4StaticRecomp.Launcher.Core.Updates;

namespace Fm4StaticRecomp.Launcher;

public sealed class LauncherStartException(string headline, string message) : Exception(message)
{
    public string Headline { get; } = headline;
}

// Everything the window drives, over the core library: Pinyon's Installation, which drove
// tools/pinyon.py, replaced.
public sealed class LauncherHost
{
    private bool _gameRunning;

    private readonly InstallLock _lock;

    private LauncherHost(string launcherDirectory, InstallLayout layout, PayloadInfo payload, InstallLock installLock)
    {
        _lock = installLock;
        LauncherDirectory = launcherDirectory;
        Layout = layout;
        Payload = payload;
        Platform = Platforms.Current;
        Settings = LauncherSettings.Load(layout.LauncherToml);
        Setup = SetupState.Load(layout.SetupState);
        Profiles = new ProfileStore(layout, Platform);
        Versions = new VersionStore(layout, Settings);
        Content = new ContentInstaller(layout, Platform);
        Runner = new SetupRunner(layout, Platform);
        ReloadFonts();
    }

    public string LauncherDirectory { get; }
    public string LauncherExecutable => Environment.ProcessPath ?? Path.Combine(LauncherDirectory, ReleaseClient.LauncherAsset);
    public InstallLayout Layout { get; }
    public IPlatform Platform { get; }
    public PayloadInfo Payload { get; }
    public LauncherSettings Settings { get; }
    public SetupState Setup { get; }
    public ProfileStore Profiles { get; }
    public VersionStore Versions { get; }
    public ContentInstaller Content { get; }
    public SetupRunner Runner { get; }
    public HttpClient Http { get; } = new() { Timeout = Timeout.InfiniteTimeSpan };
    public UiFonts? Fonts { get; private set; }

    public bool GameRunning
    {
        get => _gameRunning;
        set
        {
            if (_gameRunning == value) return;
            _gameRunning = value;
            Changed?.Invoke();
        }
    }

    public event Action? Changed;

    // lockWait: how long to wait for another launcher on this folder to exit (an update's restart).
    public static Task<LauncherHost> OpenAsync(string launcherDirectory, string launcherVersion, TimeSpan lockWait) => Task.Run(() =>
    {
        var layout = InstallLayout.ForLauncher(launcherDirectory);
        if (RootCheck.Problem(layout.Root, RootEnvironment.Current) is { } problem)
            throw new LauncherStartException("MOVE THE LAUNCHER'S FOLDER", problem);
        var installLock = InstallLock.TryAcquire(layout.Root, lockWait)
                          ?? throw new LauncherStartException("ALREADY RUNNING",
                              "Another FM4 Static Recomp launcher is using this folder. Switch to its window, or close it and start this one again.");
        PayloadInfo payload;
        try
        {
            payload = PayloadInfo.Read(launcherDirectory, launcherVersion);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or
                                       System.Text.Json.JsonException or KeyNotFoundException)
        {
            throw new LauncherStartException("SOMETHING IS MISSING", ex.Message);
        }
        try
        {
            return new LauncherHost(launcherDirectory, layout, payload, installLock);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or IOException or UnauthorizedAccessException)
        {
            throw new LauncherStartException("THE INSTALL CANNOT BE READ",
                $"{ex.Message} If {layout.SetupState} is damaged, delete it: setup then runs again, and your saves, " +
                "content and settings stay.");
        }
    });

    public void ReloadFonts() =>
        Fonts = Settings.ActiveVersion is { } version ? UiFonts.Load(Layout.UiFonts(version)) : null;

    public Task PrepareSourceAsync(CancellationToken cancellation) =>
        Versions.ExtractPayloadAsync(Payload.ZipPath, Payload.Version, cancellation);

    public DiscManifest Manifest() => DiscManifest.Load(Layout.DiscManifestOf(Payload.Version));

    public GraphicsSettings? Graphics()
    {
        if (Settings.ActiveVersion is not { } version || !File.Exists(Layout.GraphicsSchema(version))) return null;
        return new GraphicsSettings(GraphicsSchema.Load(Layout.GraphicsSchema(version)), Layout.GraphicsToml, Layout.ConfigBackups);
    }

    public static void Open(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception) { }
    }

    // Shows a file selected in its folder.
    public static void Reveal(string file)
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{file}\"") { UseShellExecute = true }); }
        catch (Exception) { }
    }
}
