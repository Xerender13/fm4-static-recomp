namespace Fm4StaticRecomp.Launcher.Core.Discs;

public sealed record DiscFile(string Path, long Size);

public interface IDiscFiles : IDisposable
{
    IReadOnlyList<DiscFile> Files { get; }
    DiscFile? Find(string path);
    Stream Open(DiscFile file);
}

public sealed class GdfDiscFiles(GdfReader reader) : IDiscFiles
{
    private readonly Dictionary<string, GdfEntry> _entries = reader.Entries.Where(e => !e.IsDirectory)
        .ToDictionary(e => e.Path, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<DiscFile> Files { get; } =
        reader.Entries.Where(e => !e.IsDirectory).Select(e => new DiscFile(e.Path, e.Size)).ToList();

    public DiscFile? Find(string path) => _entries.TryGetValue(path, out var e) ? new DiscFile(e.Path, e.Size) : null;

    public Stream Open(DiscFile file) => reader.OpenFile(_entries[file.Path]);

    public void Dispose() => reader.Dispose();
}

public sealed class FolderDiscFiles : IDiscFiles
{
    private readonly string _root;
    private readonly Dictionary<string, DiscFile> _byPath;

    public FolderDiscFiles(string root)
    {
        _root = Path.GetFullPath(root);
        // A file whose name no disc could hold (a player's own note, say) is not a disc file;
        // listing it would only make the extraction refuse its path.
        Files = Enumerate(_root)
            .Select(f => Path.GetRelativePath(_root, f).Replace(Path.DirectorySeparatorChar, '/'))
            .Where(p => p.Split('/').All(SafePath.IsSafeName))
            .Select(p => new DiscFile(p, new FileInfo(Path.Combine(_root, p)).Length))
            .ToList();
        _byPath = Files.ToDictionary(f => f.Path, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<DiscFile> Files { get; }

    public DiscFile? Find(string path) => _byPath.GetValueOrDefault(path);

    public Stream Open(DiscFile file) => File.OpenRead(SafePath.Combine(_root, _byPath[file.Path].Path));

    public void Dispose() { }

    // Linked folders are not followed, but a file is listed whatever its attributes: OneDrive
    // keeps its files as reparse points.
    private static IEnumerable<string> Enumerate(string folder)
    {
        var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = 0 };
        foreach (var file in Directory.EnumerateFiles(folder, "*", options)) yield return file;
        foreach (var directory in Directory.EnumerateDirectories(folder, "*", options))
            if (new DirectoryInfo(directory).LinkTarget is null)
                foreach (var file in Enumerate(directory)) yield return file;
    }
}
