using System.Buffers.Binary;
using System.Text;

namespace Fm4StaticRecomp.Launcher.Core.Discs;

public sealed record GdfEntry(string Path, long Size, long Offset, bool IsDirectory);

public sealed class GdfReader : IDisposable
{
    public const int SectorSize = 0x800;
    private const long DescriptorOffset = 0x10000;
    private const int MaxEntries = 200_000;
    private const int MaxDepth = 32;
    private static readonly byte[] Magic = "MICROSOFT*XBOX*MEDIA"u8.ToArray();
    // Rebuilt XISO, XGD2, XGD3, XGD1.
    private static readonly long[] PartitionOffsets = [0, 0xFD90000, 0x2080000, 0x18300000];

    private readonly Stream _image;
    private readonly List<GdfEntry> _entries = [];
    private readonly HashSet<string> _paths = new(StringComparer.OrdinalIgnoreCase);

    private GdfReader(Stream image, long @base)
    {
        _image = image;
        Base = @base;
    }

    public long Base { get; }
    public IReadOnlyList<GdfEntry> Entries => _entries;

    public static GdfReader Open(Stream image) => Open(image, []);

    // extraBases: candidates a container format suggests (GOD), tried after the known offsets.
    public static GdfReader Open(Stream image, IEnumerable<long> extraBases)
    {
        InvalidDataException? last = null;
        foreach (var candidate in PartitionOffsets.Concat(extraBases).Distinct())
        {
            if (!HasMagic(image, candidate + DescriptorOffset)) continue;
            var reader = new GdfReader(image, candidate);
            try
            {
                reader.ReadTree();
                if (reader._entries.Count > 0) return reader;
            }
            catch (InvalidDataException ex) { last = ex; }
        }
        throw last ?? new InvalidDataException("No Xbox 360 disc file system was found.");
    }

    public GdfEntry? Find(string path) =>
        _entries.FirstOrDefault(e => string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase));

    public Stream OpenFile(GdfEntry entry) => new SubStream(_image, entry.Offset, entry.Size);

    public void Dispose() => _image.Dispose();

    internal static bool HasMagic(Stream image, long offset)
    {
        if (offset < 0 || offset + Magic.Length > image.Length) return false;
        var buffer = new byte[Magic.Length];
        image.Position = offset;
        image.ReadExactly(buffer);
        return buffer.AsSpan().SequenceEqual(Magic);
    }

    private void ReadTree()
    {
        var descriptor = ReadAt(Base + DescriptorOffset, 0x20);
        var rootSector = BinaryPrimitives.ReadUInt32LittleEndian(descriptor.AsSpan(0x14));
        var rootSize = BinaryPrimitives.ReadUInt32LittleEndian(descriptor.AsSpan(0x18));
        ReadDirectory("", rootSector, rootSize, 0);
    }

    private void ReadDirectory(string prefix, uint sector, uint size, int depth)
    {
        if (size == 0) return;
        if (depth > MaxDepth) throw new InvalidDataException("The disc's folders nest too deeply.");
        var table = ReadAt(SectorOffset(sector), checked((int)size));
        var visited = new HashSet<int>();
        var pending = new Stack<int>();
        pending.Push(0);
        while (pending.Count > 0)
        {
            var offset = pending.Pop();
            if (offset + 14 > table.Length || !visited.Add(offset)) continue;
            var span = table.AsSpan(offset);
            if (BinaryPrimitives.ReadUInt32LittleEndian(span) == 0xFFFFFFFF) continue;
            var left = BinaryPrimitives.ReadUInt16LittleEndian(span);
            var right = BinaryPrimitives.ReadUInt16LittleEndian(span[2..]);
            var entrySector = BinaryPrimitives.ReadUInt32LittleEndian(span[4..]);
            var entrySize = BinaryPrimitives.ReadUInt32LittleEndian(span[8..]);
            var attributes = span[12];
            int nameLength = span[13];
            if (offset + 14 + nameLength > table.Length) throw new InvalidDataException("A folder entry is cut off.");
            var name = Encoding.ASCII.GetString(span.Slice(14, nameLength));
            if (!SafePath.IsSafeName(name)) throw new InvalidDataException($"Unsafe name on the disc: {name}");
            var path = prefix.Length == 0 ? name : prefix + "/" + name;
            var isDirectory = (attributes & 0x10) != 0;
            var entryOffset = SectorOffset(entrySector);
            if (entryOffset < 0 || entryOffset + entrySize > _image.Length)
                throw new InvalidDataException($"{path} lies outside the image.");
            if (_entries.Count >= MaxEntries) throw new InvalidDataException("The disc lists too many files.");
            if (!_paths.Add(path)) throw new InvalidDataException($"Two names on the disc differ only in case: {path}");
            _entries.Add(new GdfEntry(path, entrySize, entryOffset, isDirectory));
            if (isDirectory) ReadDirectory(path, entrySector, entrySize, depth + 1);
            if (left != 0) pending.Push(left * 4);
            if (right != 0) pending.Push(right * 4);
        }
    }

    private long SectorOffset(uint sector) => Base + (long)sector * SectorSize;

    private byte[] ReadAt(long offset, int count)
    {
        if (offset < 0 || offset + count > _image.Length) throw new InvalidDataException("A read lies outside the image.");
        var buffer = new byte[count];
        _image.Position = offset;
        _image.ReadExactly(buffer);
        return buffer;
    }
}
