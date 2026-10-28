// Adapted from Pinyon Shift's launcher (launcher/PinyonShift.Launcher.Desktop, BSD-3-Clause; see THIRD_PARTY_NOTICES.md).
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Fm4StaticRecomp.Launcher.Core.Install;

namespace Fm4StaticRecomp.Launcher;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var args = desktop.Args ?? [];
            var window = new MainWindow(args);
            // Under --skip-launcher the window opens itself only when it is needed.
            if (!args.Contains(SkipLauncherRequest.Switch)) desktop.MainWindow = window;
            // --snapshot FILE [STATE] [WIDTHxHEIGHT]: render the window to a
            // PNG and exit, to check the layout without a desktop.
            var index = Array.IndexOf(args, "--snapshot");
            if (index >= 0 && index + 1 < args.Length)
                window.Snapshot(args[index + 1], index + 2 < args.Length ? args[index + 2] : null,
                    index + 3 < args.Length ? args[index + 3] : null);
        }
        base.OnFrameworkInitializationCompleted();
    }
}
