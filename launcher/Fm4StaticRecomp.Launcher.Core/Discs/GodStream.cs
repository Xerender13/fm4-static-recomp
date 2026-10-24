namespace Fm4StaticRecomp.Launcher.Core.Discs;

// The data space of a Games-on-Demand container: every data file's 0x1000-byte blocks in
// order, without the master hash block at each file's start and the hash block before each
// run of 204 data blocks.
public sealed class GodStream : Stream
{
    private const int Block = 0x1000;
    private const int BlocksPerSubpart = 204;
    private readonly FileStream[] _files;
    private readonly long[] _blocksInFile;
    private readonly int _blocksPerFile;
    private long _position;

    public GodStream(IReadOnlyList<string> dataFiles) : this(dataFiles, 203) { }

    internal GodStream(IReadOnlyList<string> dataFiles, int subpartsPerFile)
    {
        _blocksPerFile = subpartsPerFile * BlocksPerSubpart;
        var files = new List<FileStream>();
        try
        {
            foreach (var file in dataFiles) files.Add(new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read));
        }
        catch
        {
            files.ForEach(f => f.Dispose());
            throw;
        }
        _files = files.ToArray();
        _blocksInFile = _files.Select(f => DataBlocks(f.Length)).ToArray();
        Length = _blocksInFile.Sum() * Block;
    }

    private static long DataBlocks(long fileSize)
    {
        var rest = fileSize - Block;
        if (rest <= 0) return 0;
        const long subpart = (BlocksPerSubpart + 1) * (long)Block;
        var lastBlocks = rest % subpart / Block;
        return rest / subpart * BlocksPerSubpart + Math.Max(0, lastBlocks - 1);
    }

    public override long Length { get; }
    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Position { get => _position; set => _position = Math.Clamp(value, 0, Length); }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var total = 0;
        while (count > 0 && _position < Length)
        {
            var block = _position / Block;
            var file = (int)(block / _blocksPerFile);
            var inFile = block % _blocksPerFile;
            var within = (int)(_position % Block);
            var fileOffset = Block + inFile / BlocksPerSubpart * (BlocksPerSubpart + 1L) * Block + Block +
                             inFile % BlocksPerSubpart * Block + within;
            var chunk = (int)Math.Min(Math.Min(count, Block - within), Length - _position);
            _files[file].Position = fileOffset;
            var read = _files[file].Read(buffer, offset, chunk);
            if (read == 0) break;
            total += read;
            offset += read;
            count -= read;
            _position += read;
        }
        return total;
    }

    public override long Seek(long offset, SeekOrigin origin) => Position = origin switch
    {
        SeekOrigin.Begin => offset,
        SeekOrigin.Current => _position + offset,
        _ => Length + offset,
    };

    protected override void Dispose(bool disposing)
    {
        if (disposing) foreach (var file in _files) file.Dispose();
        base.Dispose(disposing);
    }

    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
