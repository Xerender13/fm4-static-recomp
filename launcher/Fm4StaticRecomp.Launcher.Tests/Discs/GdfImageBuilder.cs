using System.Text;

namespace Fm4StaticRecomp.Launcher.Tests.Discs;

public static class GdfImageBuilder
{
    public const int Sector = 0x800;

    // files: path with '/' -> content. Directories are implied by paths.
    public static byte[] Build(IReadOnlyDictionary<string, byte[]> files, long partitionOffset = 0)
    {
        var root = new Dir();
        foreach (var (path, content) in files)
        {
            var parts = path.Split('/');
            var dir = root;
            foreach (var part in parts[..^1])
                dir = dir.Dirs.TryGetValue(part, out var sub) ? sub : dir.Dirs[part] = new Dir();
            dir.Files[parts[^1]] = content;
        }
        var image = new MemoryStream();
        long nextSector = 48;
        var subs = new Dictionary<(Dir, string), Placed>();
        var rootLayout = Layout(root, ref nextSector, subs);
        var size = partitionOffset + nextSector * Sector;
        image.SetLength(size);
        Write(image, partitionOffset, root, rootLayout, subs);
        var descriptor = new byte[Sector];
        "MICROSOFT*XBOX*MEDIA"u8.CopyTo(descriptor);
        BitConverter.GetBytes((uint)rootLayout.Sector).CopyTo(descriptor, 0x14);
        BitConverter.GetBytes((uint)rootLayout.Size).CopyTo(descriptor, 0x18);
        "MICROSOFT*XBOX*MEDIA"u8.CopyTo(descriptor.AsSpan(0x7EC));
        image.Position = partitionOffset + 0x10000;
        image.Write(descriptor);
        return image.ToArray();
    }

    private sealed class Dir
    {
        public SortedDictionary<string, Dir> Dirs { get; } = new(StringComparer.Ordinal);
        public SortedDictionary<string, byte[]> Files { get; } = new(StringComparer.Ordinal);
    }

    private sealed record Placed(long Sector, int Size, Dictionary<string, (long Sector, int Size)> Children);

    private static Placed Layout(Dir dir, ref long nextSector, Dictionary<(Dir, string), Placed> subs)
    {
        var names = dir.Dirs.Keys.Concat(dir.Files.Keys).ToList();
        var size = names.Sum(n => Align4(14 + n.Length));
        var sectors = Math.Max(1, (size + Sector - 1) / Sector);
        var placed = new Placed(nextSector, size, new());
        nextSector += sectors;
        foreach (var (name, sub) in dir.Dirs)
        {
            var child = Layout(sub, ref nextSector, subs);
            placed.Children[name] = (child.Sector, child.Size);
            subs[(dir, name)] = child;
        }
        foreach (var (name, content) in dir.Files)
        {
            placed.Children[name] = (nextSector, content.Length);
            nextSector += Math.Max(1, (content.Length + Sector - 1) / Sector);
        }
        return placed;
    }

    private static void Write(MemoryStream image, long partition, Dir dir, Placed placed,
        Dictionary<(Dir, string), Placed> subs)
    {
        var table = new byte[Math.Max(Sector, (placed.Size + Sector - 1) / Sector * Sector)];
        Array.Fill(table, (byte)0xFF);
        var offset = 0;
        var names = dir.Dirs.Keys.Concat(dir.Files.Keys).ToList();
        for (var i = 0; i < names.Count; i++)
        {
            var name = names[i];
            var entrySize = Align4(14 + name.Length);
            var right = i + 1 < names.Count ? (offset + entrySize) / 4 : 0;
            var (sector, size) = placed.Children[name];
            BitConverter.GetBytes((ushort)0).CopyTo(table, offset);
            BitConverter.GetBytes((ushort)right).CopyTo(table, offset + 2);
            BitConverter.GetBytes((uint)sector).CopyTo(table, offset + 4);
            BitConverter.GetBytes((uint)size).CopyTo(table, offset + 8);
            table[offset + 12] = dir.Dirs.ContainsKey(name) ? (byte)0x10 : (byte)0x80;
            table[offset + 13] = (byte)name.Length;
            Encoding.ASCII.GetBytes(name).CopyTo(table, offset + 14);
            for (var pad = offset + 14 + name.Length; pad < offset + entrySize; pad++) table[pad] = 0;
            offset += entrySize;
        }
        image.Position = partition + placed.Sector * Sector;
        image.Write(table, 0, table.Length);
        foreach (var (name, sub) in dir.Dirs) Write(image, partition, sub, subs[(dir, name)], subs);
        foreach (var (name, content) in dir.Files)
        {
            image.Position = partition + placed.Children[name].Sector * Sector;
            image.Write(content);
        }
    }

    private static int Align4(int value) => (value + 3) & ~3;
}
