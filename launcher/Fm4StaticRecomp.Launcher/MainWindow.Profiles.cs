using Avalonia.Controls;
using Avalonia.Interactivity;
using Fm4StaticRecomp.Launcher.Core.Profiles;

namespace Fm4StaticRecomp.Launcher;

// The profile picker beside Play and its menu (spec 7): one save each, switched before Play.
public partial class MainWindow
{
    private bool _loadingProfiles;

    private Profile? SelectedProfile => ProfileCombo.SelectedItem is ComboBoxItem { Tag: Profile profile } ? profile : null;

    private void ReloadProfiles()
    {
        if (_host is not { } host) return;
        _loadingProfiles = true;
        var profiles = host.Profiles.List();
        ProfileCombo.Items.Clear();
        foreach (var profile in profiles)
            ProfileCombo.Items.Add(new ComboBoxItem { Content = profile.Name, Tag = profile });
        var active = profiles.FirstOrDefault(p => p.Id == host.Settings.ActiveProfile) ?? profiles.FirstOrDefault();
        ProfileCombo.SelectedIndex = active is null ? -1 : profiles.ToList().IndexOf(active);
        _loadingProfiles = false;
        if (active is not null && host.Settings.ActiveProfile != active.Id) SaveActiveProfile(active);
    }

    private void SaveActiveProfile(Profile profile)
    {
        _host!.Settings.ActiveProfile = profile.Id;
        _host.Settings.Save();
    }

    private void ProfileCombo_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (_loadingProfiles || _host is null || SelectedProfile is not { } profile || _host.GameRunning) return;
        if (_host.Settings.ActiveProfile != profile.Id) SaveActiveProfile(profile);
        UpdateActions();
    }

    private async Task ProfileActionAsync(Func<LauncherHost, Task> action)
    {
        if (_host is not { } host || host.GameRunning || _busy) return;
        try
        {
            await action(host);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            await Dialog.AskAsync(this, "PROFILE", error.Message, "OK");
        }
        ReloadProfiles();
    }

    private async void NewProfile_Click(object? sender, RoutedEventArgs e) => await ProfileActionAsync(async host =>
    {
        var (answer, values) = await Dialog.PromptAsync(this, "NEW PROFILE",
            "A separate save: its own career, garage and gamertag. Disc 2, DLC and graphics settings are shared.",
            [("Name", "")], "CREATE", "CANCEL");
        if (answer != "CREATE" || values[0].Trim().Length == 0) return;
        var profile = host.Profiles.Create(values[0]);
        SaveActiveProfile(profile);
    });

    private async void RenameProfile_Click(object? sender, RoutedEventArgs e) => await ProfileActionAsync(async host =>
    {
        if (SelectedProfile is not { } profile) return;
        var (name, gamertag) = (profile.Name, profile.Gamertag);
        var message = "";
        while (true)
        {
            var (answer, values) = await Dialog.PromptAsync(this, "RENAME PROFILE", message,
                [("Name", name), ("Gamertag", gamertag)], "SAVE", "CANCEL");
            if (answer != "SAVE") return;
            (name, gamertag) = (values[0], values[1].Trim());
            if (name.Trim().Length > 0 && ProfileStore.IsValidGamertag(gamertag)) break;
            message = "A gamertag has 1 to 15 English letters, digits or spaces and starts with a letter; the name cannot be empty.";
        }
        host.Profiles.Rename(profile.Id, name, gamertag);
    });

    private void OpenSaveFolder_Click(object? sender, RoutedEventArgs e)
    {
        if (SelectedProfile is not { } profile) return;
        Directory.CreateDirectory(profile.User);
        LauncherHost.Open(profile.User);
    }

    private async void ImportSave_Click(object? sender, RoutedEventArgs e) => await ProfileActionAsync(async host =>
    {
        if (await PickFolderAsync("Choose a state folder or its user folder to import") is not { } folder) return;
        var (answer, values) = await Dialog.PromptAsync(this, "IMPORT SAVE",
            $"Copies the saves in {folder} into a new profile. The folder is not changed.",
            [("Name", Path.GetFileName(Path.TrimEndingDirectorySeparator(folder)))], "IMPORT", "CANCEL");
        if (answer != "IMPORT" || values[0].Trim().Length == 0) return;
        SetBusy(true);
        try
        {
            var profile = await Task.Run(() => host.Profiles.Import(folder, values[0]));
            SaveActiveProfile(profile);
        }
        finally
        {
            SetBusy(false);
        }
    });

    private async void DeleteProfile_Click(object? sender, RoutedEventArgs e) => await ProfileActionAsync(async host =>
    {
        if (SelectedProfile is not { } profile) return;
        var (answer, values) = await Dialog.PromptAsync(this, "DELETE PROFILE",
            $"Moves {profile.Name} to data\\deleted; it can be restored from there by hand. Type its name to confirm.",
            [("Name", "")], "DELETE", "CANCEL");
        if (answer != "DELETE" || values[0] != profile.Name) return;
        host.Profiles.Delete(profile.Id);
        if (host.Settings.ActiveProfile == profile.Id)
        {
            host.Settings.ActiveProfile = null;
            host.Settings.Save();
        }
    });
}
