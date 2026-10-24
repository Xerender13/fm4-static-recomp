namespace Fm4StaticRecomp.Launcher.Core.Discs;

public sealed record GodContainer(string HeaderPath, IReadOnlyList<string> DataFiles, PackageHeader Header)
{
    internal int SubpartsPerFile { get; init; } = 203;

    public static GodContainer? Find(string folder) => Find(folder, 203);

    internal static GodContainer? Find(string folder, int subpartsPerFile)
    {
        foreach (var file in Directory.EnumerateFiles(folder, "*", DiscInput.Everything))
        {
            var dataFolder = file + ".data";
            if (!Directory.Exists(dataFolder)) continue;
            var header = PackageHeader.TryRead(file);
            if (header is null || header.ContentType != PackageHeader.GamesOnDemand) continue;
            var data = Directory.GetFiles(dataFolder, "Data????").Order(StringComparer.OrdinalIgnoreCase).ToList();
            if (data.Count == 0) continue;
            var full = 0x1000L + subpartsPerFile * 205L * 0x1000;
            if (data.SkipLast(1).Any(d => new FileInfo(d).Length != full)) continue;
            return new GodContainer(file, data, header) { SubpartsPerFile = subpartsPerFile };
        }
        return null;
    }

    public Stream OpenData() => new GodStream(DataFiles, SubpartsPerFile);

    // GOD data may start at the GDF partition or at its volume descriptor (XGD3's enhanced
    // layout); each magic found near the start is tried as the descriptor.
    public GdfReader OpenGdf()
    {
        var stream = OpenData();
        try
        {
            var candidates = new List<long>();
            for (long offset = 0; offset < Math.Min(stream.Length, 0x40000); offset += GdfReader.SectorSize)
                if (GdfReader.HasMagic(stream, offset)) candidates.Add(offset - 0x10000);
            return GdfReader.Open(stream, candidates);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }
}
