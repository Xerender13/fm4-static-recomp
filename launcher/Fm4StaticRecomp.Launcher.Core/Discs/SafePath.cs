namespace Fm4StaticRecomp.Launcher.Core.Discs;

public static class SafePath
{
    public static bool IsSafeName(string name) =>
        name.Length is > 0 and <= 255 && name is not "." and not ".." &&
        name.All(c => c >= 0x20 && c < 0x7F && c is not '/' and not '\\' and not ':' and not '*'
            and not '?' and not '"' and not '<' and not '>' and not '|');

    public static string Combine(string root, string relativeWithSlashes)
    {
        var parts = relativeWithSlashes.Split('/');
        if (parts.Any(p => !IsSafeName(p))) throw new InvalidDataException($"Unsafe path: {relativeWithSlashes}");
        var full = Path.GetFullPath(Path.Combine([root, .. parts]));
        var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Unsafe path: {relativeWithSlashes}");
        return full;
    }
}
