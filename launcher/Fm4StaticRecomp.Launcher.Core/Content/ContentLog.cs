using System.Text.RegularExpressions;

namespace Fm4StaticRecomp.Launcher.Core.Content;

public abstract record ContentEvent
{
    public sealed record Installing(string Name) : ContentEvent;
    public sealed record Installed(string Name) : ContentEvent;
    public sealed record Skipped(string Name) : ContentEvent;
    public sealed record Failed(string Name, string Code) : ContentEvent;
    // The counts share their names with the event types above, which they hide here.
    public sealed record Finished(int Installed, int Skipped, int Failed) : ContentEvent
    {
        public new int Installed { get; init; } = Installed;
        public new int Skipped { get; init; } = Skipped;
        public new int Failed { get; init; } = Failed;
    }
}

public static partial class ContentLog
{
    public static ContentEvent? Parse(string line)
    {
        var index = line.IndexOf("Content: ", StringComparison.Ordinal);
        if (index < 0) return null;
        var text = line[(index + "Content: ".Length)..].TrimEnd();
        if (Summary().Match(text) is { Success: true } s)
            return new ContentEvent.Finished(int.Parse(s.Groups[1].Value), int.Parse(s.Groups[2].Value),
                int.Parse(s.Groups[3].Value));
        if (FailedLine().Match(text) is { Success: true } f)
            return new ContentEvent.Failed(Path.GetFileName(f.Groups[1].Value), f.Groups[2].Value);
        if (text.StartsWith("installed ")) return new ContentEvent.Installed(text["installed ".Length..]);
        if (text.StartsWith("skipped ")) return new ContentEvent.Skipped(text["skipped ".Length..].Replace(" (installed)", ""));
        if (text.StartsWith("installing ")) return new ContentEvent.Installing(Path.GetFileName(text["installing ".Length..]));
        return null;
    }

    [GeneratedRegex(@"^install finished, (\d+) installed, (\d+) skipped, (\d+) failed$")]
    private static partial Regex Summary();

    [GeneratedRegex(@"^installing (.+) failed: ([0-9A-Fa-f]+)$")]
    private static partial Regex FailedLine();
}
