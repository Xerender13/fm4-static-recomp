using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Fm4StaticRecomp.Launcher.Controls;
using Fm4StaticRecomp.Launcher.Core.Content;
using Fm4StaticRecomp.Launcher.Core.Discs;
using Fm4StaticRecomp.Launcher.Core.Install;
using Fm4StaticRecomp.Launcher.Core.Setup;
using Fm4StaticRecomp.Launcher.Core.Updates;

namespace Fm4StaticRecomp.Launcher;

// First-run setup (spec 5): five steps, each resumed from setup-state.json when it is done.
public partial class MainWindow
{
    private enum Step { Disc1, Disc2, Licence, Build, Profile }
    private enum StepState { Waiting, Active, Done, Failed }

    private DiscInput? _disc1Input;
    private DiscInput? _disc2Input;
    private bool _disc2Later;
    private int _profileCount;
    private readonly Dictionary<Step, (StepState State, string Detail)> _stepOverrides = [];

    private (TextBlock Mark, TextBlock Title, TextBlock Detail) Row(Step step) => step switch
    {
        Step.Disc1 => (Disc1Mark, Disc1Title, Disc1Detail),
        Step.Disc2 => (Disc2Mark, Disc2Title, Disc2Detail),
        Step.Licence => (LicenceMark, LicenceTitle, LicenceDetail),
        Step.Build => (BuildMark, BuildTitle, BuildDetail),
        _ => (ProfileMark, ProfileTitle, ProfileDetail),
    };

    private void SetStep(Step step, StepState state, string detail)
    {
        var (mark, title, text) = Row(step);
        mark.Text = state switch { StepState.Done => "✓", StepState.Failed => "✕", StepState.Active => "●", _ => "○" };
        mark.Classes.Set("done", state == StepState.Done);
        mark.Classes.Set("active", state == StepState.Active);
        mark.Classes.Set("failed", state == StepState.Failed);
        title.Classes.Set("waiting", state == StepState.Waiting);
        text.Text = detail;
    }

    // Overrides hold what this session's run found (a failure, the step running now) over what
    // setup-state.json says.
    private void MarkStep(Step step, StepState state, string detail)
    {
        _stepOverrides[step] = (state, detail);
        SetStep(step, state, detail);
    }

    private void UpdateSetupRows()
    {
        if (_host is not { } host) return;
        var setup = host.Setup;
        var version = host.Payload.Version;
        _profileCount = host.Profiles.List().Count;

        SetStep(Step.Disc1, setup.Disc1Installed ? StepState.Done : StepState.Waiting,
            setup.Disc1Installed ? $"{setup.Disc1Release} installed."
            : _disc1Input is { } disc1 ? $"Chosen: {DisplayName(disc1.Path)} ({KindText(disc1.Kind)}). Verified and extracted when setup starts."
            : "An ISO image, a GOD folder or an extracted folder of your FM4 Disc 1.");
        Disc1Buttons.IsVisible = !setup.Disc1Installed;

        var (disc2State, disc2Text) =
            setup.Disc2Installed ? (StepState.Done, "Installed.")
            : _disc2Input is { } disc2 ? (StepState.Waiting, $"Chosen: {DisplayName(disc2.Path)} ({KindText(disc2.Kind)}). Verified now, installed after the build.")
            : _disc2Later || setup.Disc2Later ? (StepState.Done, "Later: add it from CONTENT once the game is built.")
            : setup.Disc2Source is { } source ? (StepState.Done, $"Verified: {source}. Installed after the build; keep it there until then.")
            : (StepState.Waiting, "An ISO image, a GOD folder or the folder with its four packages. Optional.");
        SetStep(Step.Disc2, disc2State, disc2Text);
        Disc2Buttons.IsVisible = !setup.Disc2Installed;

        var settings = host.Settings;
        SetStep(Step.Licence, settings.UseVisualStudioInstaller || settings.AcceptedMicrosoftLicence is not null ? StepState.Done : StepState.Waiting,
            settings.UseVisualStudioInstaller ? "Microsoft's installer: it asks for administrator permission once."
            : settings.AcceptedMicrosoftLicence is not null ? "Accepted. Asked again only when Microsoft's licences change."
            : "Asked before the build: the build downloads Microsoft's C++ runtime and Windows SDK files.");

        var built = setup.BuiltVersions.Contains(version);
        SetStep(Step.Build, built ? StepState.Done : StepState.Waiting,
            built ? $"Version {version} built."
            : "Downloads the build tools, then translates and compiles the game on this computer.");

        var profiles = host.Profiles.List();
        SetStep(Step.Profile, profiles.Count > 0 ? StepState.Done : StepState.Waiting,
            profiles.Count > 0 ? string.Join(", ", profiles.Select(p => p.Name)) : "The gamertag starts as this name.");
        ProfileNameBox.IsVisible = profiles.Count == 0;

        foreach (var (step, (state, detail)) in _stepOverrides) SetStep(step, state, detail);

        var needed = SpaceCheck.Needed(!setup.Disc1Installed, DiscTwoPending(host), !Directory.Exists(host.Layout.Toolchain), !built);
        InstallText.Text = string.Create(CultureInfo.InvariantCulture,
            $"Installs to {host.Layout.Root}. About {needed / 1e9:0} GB are needed and {FreeBytes(host.Layout.Root) / 1e9:0} GB are free.");
    }

    private bool DiscTwoPending(LauncherHost host) =>
        !host.Setup.Disc2Installed && (_disc2Input is not null || (!_disc2Later && host.Setup.Disc2Source is not null));

    private static long FreeBytes(string root) =>
        new DriveInfo(Path.GetPathRoot(Path.GetFullPath(root))!).AvailableFreeSpace;

    private static string DisplayName(string path) => Path.GetFileName(Path.TrimEndingDirectorySeparator(path));

    private static string KindText(DiscInputKind kind) => kind switch
    {
        DiscInputKind.Iso => "disc image",
        DiscInputKind.God => "GOD folder",
        DiscInputKind.ExtractedFolder => "extracted folder",
        _ => "packages folder",
    };

    private bool DropsGoToDisc1 => _host is { } host && !host.Setup.Disc1Installed && _disc1Input is null;

    private bool SetupCanStart()
    {
        if (_host is not { } host) return false;
        var setup = host.Setup;
        var disc1 = setup.Disc1Installed || _disc1Input is not null;
        var disc2 = setup.Disc2Installed || setup.Disc2Source is not null || setup.Disc2Later || _disc2Input is not null || _disc2Later;
        var profile = _profileCount > 0 || !string.IsNullOrWhiteSpace(ProfileNameBox.Text);
        return disc1 && disc2 && profile && OwnershipCheck.IsChecked == true;
    }

    private void UpdateSetupActions()
    {
        var open = !_busy && _mode == Mode.Setup;
        BrowseButton.IsEnabled = BrowseFolderButton.IsEnabled = open;
        BrowseDisc2Button.IsEnabled = BrowseDisc2FolderButton.IsEnabled = Disc2LaterButton.IsEnabled = open;
        ProfileNameBox.IsEnabled = OwnershipCheck.IsEnabled = open;
    }

    private void Ownership_Changed(object? sender, RoutedEventArgs e) => UpdateActions();

    private void ProfileName_Changed(object? sender, TextChangedEventArgs e) => UpdateActions();

    private async void Browse_Click(object? sender, RoutedEventArgs e)
    {
        if (await PickFileAsync("Choose your FM4 Disc 1 image") is { } path) await ChooseDisc1Async(path);
    }

    private async void BrowseFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (await PickFolderAsync("Choose your FM4 Disc 1 GOD folder or extracted folder") is { } path) await ChooseDisc1Async(path);
    }

    private async void BrowseDisc2_Click(object? sender, RoutedEventArgs e)
    {
        if (await PickFileAsync("Choose your FM4 Disc 2 image") is { } path) await ChooseDisc2Async(path);
    }

    private async void BrowseDisc2Folder_Click(object? sender, RoutedEventArgs e)
    {
        if (await PickFolderAsync("Choose your FM4 Disc 2 GOD folder or the folder with its packages") is { } path)
            await ChooseDisc2Async(path);
    }

    private void Disc2Later_Click(object? sender, RoutedEventArgs e)
    {
        _disc2Input = null;
        _disc2Later = true;
        _stepOverrides.Remove(Step.Disc2);
        UpdateSetupRows();
        UpdateActions();
    }

    private async Task<string?> PickFileAsync(string title)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter =
            [
                new FilePickerFileType("Disc image") { Patterns = ["*.iso", "*.ISO"] },
                FilePickerFileTypes.All,
            ],
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    private async Task<string?> PickFolderAsync(string title)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false });
        return folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
    }

    private async Task ChooseDisc1Async(string path)
    {
        SetBusy(true);
        var input = await Task.Run(() => DiscInput.Detect(path));
        SetBusy(false);
        _stepOverrides.Remove(Step.Disc1);
        if (input is null or { Kind: DiscInputKind.PackageFolder })
        {
            _disc1Input = null;
            UpdateSetupRows();
            MarkStep(Step.Disc1, StepState.Failed, $"{DisplayName(path)} is not an FM4 Disc 1 image, GOD folder or extracted folder.");
        }
        else
        {
            _disc1Input = input;
            AppendLog($"Disc 1: {input.Path} ({KindText(input.Kind)}).");
            UpdateSetupRows();
        }
        UpdateActions();
    }

    private async Task ChooseDisc2Async(string path)
    {
        SetBusy(true);
        var input = await Task.Run(() => DiscInput.Detect(path));
        SetBusy(false);
        _stepOverrides.Remove(Step.Disc2);
        if (input is null or { Kind: DiscInputKind.ExtractedFolder })
        {
            _disc2Input = null;
            UpdateSetupRows();
            MarkStep(Step.Disc2, StepState.Failed, $"{DisplayName(path)} is not an FM4 Disc 2 image, GOD folder or packages folder.");
        }
        else
        {
            _disc2Input = input;
            _disc2Later = false;
            AppendLog($"Disc 2: {input.Path} ({KindText(input.Kind)}).");
            UpdateSetupRows();
        }
        UpdateActions();
    }

    // VERIFY AND BUILD: every step not done yet, in order. Each step saves setup-state.json when
    // it is done, so a stop anywhere resumes at the first step not done.
    private async Task RunSetupAsync(CancellationToken cancellation)
    {
        var host = _host!;
        _stepOverrides.Clear();
        StatusText.Text = "";
        AltButton.IsVisible = false;
        ProgressMessage.Text = "";
        SetProgress(-1, "Preparing the source.");
        try
        {
            await host.PrepareSourceAsync(cancellation);
            var manifest = await Task.Run(host.Manifest, cancellation);
            var needed = SpaceCheck.Needed(!host.Setup.Disc1Installed, DiscTwoPending(host), !Directory.Exists(host.Layout.Toolchain),
                !host.Setup.BuiltVersions.Contains(host.Payload.Version));
            if (SpaceCheck.Problem(host.Layout.Root, needed, FreeBytes) is { } space)
            {
                ProgressPanel.IsVisible = false;
                await Dialog.AskAsync(this, "NOT ENOUGH SPACE", space, "OK");
                return;
            }
            if (!await InstallDisc1Async(host, manifest, cancellation)) return;
            if (!await VerifyDisc2Async(host, manifest, cancellation)) return;
            if (!host.Setup.BuiltVersions.Contains(host.Payload.Version))
            {
                if (await AskLicenceAsync(host, cancellation) is not { } accepted) return;
                if (!await BuildAsync(host, accepted, cancellation)) return;
            }
            var disc2Problem = await InstallDisc2Async(host, manifest, cancellation);
            if (!await CreateProfileAsync(host)) return;
            _stepOverrides.Clear();
            ShowMode(Mode.Ready);
            if (disc2Problem is not null)
            {
                StatusText.Foreground = Brush("AccentBrush");
                StatusText.Text = $"Disc 2 was not installed: {disc2Problem}";
            }
        }
        catch (OperationCanceledException)
        {
            foreach (var step in _stepOverrides.Where(o => o.Value.State == StepState.Active).Select(o => o.Key).ToList())
                _stepOverrides.Remove(step);
            ShowMode(Mode.Setup);
            ProgressPanel.IsVisible = false;
            StatusText.Text = "Stopped. Start again to resume where it stopped.";
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException)
        {
            ShowFailure("SETUP STOPPED", error.Message, Mode.Setup);
            PrimaryButton.Content = "RETRY";
        }
    }

    private async Task<bool> InstallDisc1Async(LauncherHost host, DiscManifest manifest, CancellationToken cancellation)
    {
        if (host.Setup.Disc1Installed) return true;
        var input = _disc1Input!;
        MarkStep(Step.Disc1, StepState.Active, $"Verifying and extracting {DisplayName(input.Path)}.");
        var progress = new Progress<double>(p => SetProgress(p * 100, $"Disc 1: verifying and extracting ({p:P0})."));
        var outcome = await Task.Run(() => Disc1Step.InstallAsync(input, manifest, host.Layout.Game, AcceptDisc1ProblemsAsync, progress,
            cancellation), cancellation);
        switch (outcome)
        {
            case Disc1Outcome.Refused refused:
                var check = refused.Check;
                var found = check.FoundSha256.Length > 0 ? check.FoundSha256 : "no default.xex";
                var text = check.Release is null
                    ? $"This disc's media ID is {(check.MediaId.Length > 0 ? check.MediaId : "unknown")} and its default.xex has SHA-256 {found}; " +
                      $"the launcher supports {string.Join(" and ", manifest.Disc1.Releases.Select(r => r.Name))}. Rev 1 discs are not supported."
                    : $"{check.Release.Name}'s default.xex should have SHA-256 {check.ExpectedSha256}; this one has {found}.";
                AppendLog($"Disc 1 refused: {text}");
                ProgressPanel.IsVisible = false;
                _disc1Input = null;
                MarkStep(Step.Disc1, StepState.Failed, "Not a supported Disc 1.");
                await Dialog.AskAsync(this, "NOT A SUPPORTED DISC 1", text, "OK");
                return false;
            case Disc1Outcome.Installed installed:
                host.Setup.Disc1Release = installed.ReleaseName;
                host.Setup.Disc1ProblemsAccepted = installed.ProblemsAccepted;
                host.Setup.Save();
                _disc1Input = null;
                _stepOverrides.Remove(Step.Disc1);
                AppendLog($"Disc 1: {installed.ReleaseName} installed ({installed.ProblemsAccepted} differing files accepted).");
                UpdateSetupRows();
                return true;
            default:
                ProgressPanel.IsVisible = false;
                MarkStep(Step.Disc1, StepState.Waiting, "Cancelled: nothing was installed.");
                return false;
        }
    }

    private Task<bool> AcceptDisc1ProblemsAsync(IReadOnlyList<FileProblem> problems) =>
        Dispatcher.UIThread.InvokeAsync(async () =>
            await Dialog.AskAsync(this, "SOME FILES DIFFER", ProblemList(problems, "This disc differs from the known one:"),
                "CONTINUE ANYWAY", "CANCEL") == "CONTINUE ANYWAY");

    private static string ProblemList(IReadOnlyList<FileProblem> problems, string heading)
    {
        var lines = problems.Take(20).Select(p => $"{p.Path} ({p.Kind switch
        {
            FileProblemKind.Missing => "missing",
            FileProblemKind.SizeMismatch => "size",
            _ => "changed",
        }})").ToList();
        if (problems.Count > 20) lines.Add($"+ {problems.Count - 20} more");
        return heading + "\n" + string.Join("\n", lines);
    }

    private async Task<bool> VerifyDisc2Async(LauncherHost host, DiscManifest manifest, CancellationToken cancellation)
    {
        var setup = host.Setup;
        if (setup.Disc2Installed) return true;
        if (_disc2Input is not { } input)
        {
            if (_disc2Later)
            {
                setup.Disc2Later = true;
                setup.Disc2Source = null;
                setup.Save();
            }
            return true;
        }
        MarkStep(Step.Disc2, StepState.Active, $"Verifying {DisplayName(input.Path)}.");
        var progress = new Progress<double>(p => SetProgress(p * 100, $"Disc 2: verifying ({p:P0})."));
        var problems = await Disc2Source.VerifyAsync(input, manifest, progress, cancellation);
        if (problems.Count > 0)
        {
            var answer = await Dialog.AskAsync(this, "DISC 2 DIFFERS", ProblemList(problems, "These packages differ from the known ones:"),
                "USE IT ANYWAY", "LATER");
            if (answer is null)
            {
                MarkStep(Step.Disc2, StepState.Waiting, "Not verified.");
                ProgressPanel.IsVisible = false;
                return false;
            }
            if (answer == "LATER")
            {
                _disc2Input = null;
                _disc2Later = true;
                setup.Disc2Later = true;
                setup.Disc2Source = null;
                setup.Save();
                _stepOverrides.Remove(Step.Disc2);
                UpdateSetupRows();
                return true;
            }
        }
        setup.Disc2Source = input.Path;
        setup.Disc2Later = false;
        setup.Save();
        _disc2Input = null;
        _stepOverrides.Remove(Step.Disc2);
        AppendLog($"Disc 2 verified: {input.Path} ({problems.Count} differing packages accepted).");
        UpdateSetupRows();
        return true;
    }

    // Null when setup stops here; else whether the feed's licence was accepted (false: the
    // Visual Studio installer route).
    private async Task<bool?> AskLicenceAsync(LauncherHost host, CancellationToken cancellation)
    {
        var settings = host.Settings;
        if (settings.UseVisualStudioInstaller) return false;
        MarkStep(Step.Licence, StepState.Active, "Reading Microsoft's licences.");
        SetProgress(-1, "Reading Microsoft's licences.");
        MicrosoftLicences? licences = null;
        while (licences is null)
        {
            try
            {
                licences = await MicrosoftLicences.ReadAsync(host.Layout.LicencesScript(host.Payload.Version), cancellation);
            }
            catch (InvalidOperationException error)
            {
                if (await Dialog.AskAsync(this, "THE LICENCES COULD NOT BE READ", error.Message, "RETRY", "STOP") != "RETRY")
                {
                    MarkStep(Step.Licence, StepState.Waiting, "Not read.");
                    ProgressPanel.IsVisible = false;
                    return null;
                }
            }
        }
        if (licences.AcceptedIn(settings))
        {
            _stepOverrides.Remove(Step.Licence);
            return true;
        }
        var answer = await Dialog.AskAsync(this, "MICROSOFT'S BUILD FILES",
            "The build needs Microsoft's C++ runtime and Windows SDK files. Your computer downloads them from Microsoft; " +
            "nothing of Microsoft's comes with this launcher. Read and accept their licences to continue.",
            [("BUILD TOOLS LICENCE", licences.BuildTools), ("WINDOWS SDK LICENCE", licences.WindowsSdk)], "I ACCEPT", "DECLINE");
        if (answer == "I ACCEPT")
        {
            settings.AcceptedMicrosoftLicence = licences.Key;
            settings.Save();
            AppendLog("Microsoft's licences accepted.");
            _stepOverrides.Remove(Step.Licence);
            UpdateSetupRows();
            return true;
        }
        if (await Dialog.AskAsync(this, "USE MICROSOFT'S INSTALLER?",
                "Use Microsoft's installer instead? It installs the same files system-wide and asks for administrator permission once.",
                "USE THE INSTALLER", "STOP") == "USE THE INSTALLER")
        {
            settings.UseVisualStudioInstaller = true;
            settings.Save();
            _stepOverrides.Remove(Step.Licence);
            UpdateSetupRows();
            return false;
        }
        MarkStep(Step.Licence, StepState.Waiting, "Declined: setup needs the licence or Microsoft's installer.");
        ProgressPanel.IsVisible = false;
        return null;
    }

    private async Task<bool> BuildAsync(LauncherHost host, bool licenceAccepted, CancellationToken cancellation)
    {
        var version = host.Payload.Version;
        ShowMode(Mode.Building);
        MarkStep(Step.Build, StepState.Active, $"Building version {version}.");
        SetProgress(0, "Starting the build.");
        var progress = new Progress<SetupEvent>(e =>
        {
            var overall = SetupProgress.Overall(e);
            if (overall >= 0) SetProgress(overall, e.Message);
            MarkStep(Step.Build, StepState.Active, $"{e.Stage}: {e.Message}");
            AppendLog($"[{e.Stage} {e.Percent}%] {e.Message}");
        });
        var request = new SetupRequest(version, host.Settings.BuildJobs, licenceAccepted, host.Settings.UseVisualStudioInstaller);
        var failure = await new VersionBuilder(host.Layout, host.Versions, host.Setup, host.Runner)
            .BuildVersionAsync(request, host.Payload.ZipPath, progress, AppendLog, cancellation);
        if (failure is not null)
        {
            _stepOverrides[Step.Build] = (StepState.Failed, failure.Step is { } step ? $"Stopped at {step}." : "Stopped.");
            ShowFailure("THE BUILD STOPPED", FailureText(failure), Mode.Setup);
            PrimaryButton.Content = "RETRY";
            if (failure.OffersVisualStudioInstaller)
                ShowAltButton("USE MICROSOFT'S INSTALLER", async () =>
                {
                    host.Settings.UseVisualStudioInstaller = true;
                    host.Settings.Save();
                    await RunWorkAsync(RunSetupAsync);
                });
            return false;
        }
        _stepOverrides.Remove(Step.Build);
        host.ReloadFonts();
        VectorHeading.Fonts = host.Fonts;
        AppendLog($"Version {version} built.");
        UpdateSetupRows();
        return true;
    }

    private static string FailureText(SetupFailure failure)
    {
        var lines = new List<string> { failure.ExitCode != 0 ? $"{failure.Message} (exit {failure.ExitCode})" : failure.Message };
        if (failure.Hint is { Length: > 0 } hint) lines.Add(hint);
        lines.AddRange(failure.Excerpt.Take(5));
        lines.Add($"Logs: {failure.LogFolder}");
        return string.Join("\n", lines);
    }

    // Disc 2 installs through the built game, so after the build, from the source verified
    // earlier; a source that is gone by now is asked for again (spec 5, step 2). Returns why
    // it was not installed, for Ready, which hides the setup rows.
    private async Task<string?> InstallDisc2Async(LauncherHost host, DiscManifest manifest, CancellationToken cancellation)
    {
        var setup = host.Setup;
        if (setup.Disc2Installed || setup.Disc2Source is not { } source) return null;
        var verified = source;
        DiscInput? input;
        while ((input = await Task.Run(() => DiscInput.Detect(source), cancellation)) is null or { Kind: DiscInputKind.ExtractedFolder })
        {
            var answer = await Dialog.AskAsync(this, "DISC 2 IS NOT THERE",
                $"{source} is not there any more. Choose it again or install it later from CONTENT.",
                "CHOOSE IMAGE", "CHOOSE FOLDER", "LATER");
            var chosen = answer switch
            {
                "CHOOSE IMAGE" => await PickFileAsync("Choose your FM4 Disc 2 image"),
                "CHOOSE FOLDER" => await PickFolderAsync("Choose your FM4 Disc 2 GOD folder or the folder with its packages"),
                _ => null,
            };
            if (answer is "CHOOSE IMAGE" or "CHOOSE FOLDER")
            {
                if (chosen is not null) source = chosen;
                continue;
            }
            setup.Disc2Source = null;
            setup.Disc2Later = true;
            setup.Save();
            UpdateSetupRows();
            return null;
        }
        var staging = Path.Combine(host.Layout.Downloads, "disc2");
        try
        {
            MarkStep(Step.Disc2, StepState.Active, "Copying the packages.");
            SetProgress(-1, "Disc 2: copying the packages.");
            var staged = await Disc2Source.StageAsync(input, manifest, staging, cancellation);
            if (staged.Problems.Count > 0 && !string.Equals(source, verified, StringComparison.OrdinalIgnoreCase) &&
                await Dialog.AskAsync(this, "DISC 2 DIFFERS", ProblemList(staged.Problems, "These packages differ from the known ones:"),
                    "USE IT ANYWAY", "LATER") != "USE IT ANYWAY")
            {
                setup.Disc2Source = null;
                setup.Disc2Later = true;
                setup.Save();
                _stepOverrides.Remove(Step.Disc2);
                UpdateSetupRows();
                return null;
            }
            setup.Disc2Source = source;
            setup.Save();
            var progress = new Progress<ContentEvent>(e =>
            {
                var text = ContentText(e);
                if (text.Length == 0) return;
                MarkStep(Step.Disc2, StepState.Active, text);
                SetProgress(-1, $"Disc 2: {text}");
                AppendLog($"Disc 2: {text}");
            });
            var finished = await host.Content.InstallAsync(host.Payload.Version, staging, null, progress, cancellation);
            if (staged.Complete && finished.Failed == 0)
            {
                setup.Disc2Installed = true;
                setup.Save();
                _stepOverrides.Remove(Step.Disc2);
                UpdateSetupRows();
                return null;
            }
            return Disc2Failed(!staged.Complete
                ? $"{staged.Problems.Count(p => p.Kind == FileProblemKind.Missing)} packages were missing. Add Disc 2 again from CONTENT."
                : $"{finished.Failed} packages failed; see logs\\content-install.log. Add Disc 2 again from CONTENT.");
        }
        catch (InvalidOperationException error)
        {
            return Disc2Failed($"{error.Message} Add Disc 2 again from CONTENT.");
        }
        finally
        {
            await DeleteQuietlyAsync(staging);
        }
    }

    private string Disc2Failed(string detail)
    {
        MarkStep(Step.Disc2, StepState.Failed, detail);
        AppendLog($"Disc 2 not installed: {detail}");
        return detail;
    }

    private static Task DeleteQuietlyAsync(string folder) => Task.Run(() =>
    {
        try { if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    });

    private static string ContentText(ContentEvent e) => e switch
    {
        ContentEvent.Installing i => $"Installing {i.Name}…",
        ContentEvent.Installed i => $"Installed {i.Name}",
        ContentEvent.Skipped s => $"{s.Name} was already installed",
        ContentEvent.Failed f => $"{f.Name} failed ({f.Code})",
        ContentEvent.Finished f => $"Done: {f.Installed} installed, {f.Skipped} already there, {f.Failed} failed.",
        _ => "",
    };

    private async Task<bool> CreateProfileAsync(LauncherHost host)
    {
        if (host.Profiles.List().Count > 0) return true;
        try
        {
            var profile = host.Profiles.Create(ProfileNameBox.Text!.Trim());
            host.Settings.ActiveProfile = profile.Id;
            host.Settings.Save();
            AppendLog($"Profile {profile.Name} created ({profile.Id}).");
            return true;
        }
        catch (IOException error)
        {
            MarkStep(Step.Profile, StepState.Failed, error.Message);
            ProgressPanel.IsVisible = false;
            await Dialog.AskAsync(this, "THE PROFILE COULD NOT BE CREATED", error.Message, "OK");
            return false;
        }
    }
}
