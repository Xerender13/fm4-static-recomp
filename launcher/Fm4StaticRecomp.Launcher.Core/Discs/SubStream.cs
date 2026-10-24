namespace Fm4StaticRecomp.Launcher.Core.Discs;

internal sealed class SubStream(Stream inner, long start, long length) : Stream
{
    private long _position;

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => length;
    public override long Position { get => _position; set => _position = Math.Clamp(value, 0, length); }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var remaining = length - _position;
        if (remaining <= 0) return 0;
        inner.Position = start + _position;
        var read = inner.Read(buffer, offset, (int)Math.Min(count, remaining));
        _position += read;
        return read;
    }

    public override long Seek(long offset, SeekOrigin origin) => Position = origin switch
    {
        SeekOrigin.Begin => offset,
        SeekOrigin.Current => _position + offset,
        _ => length + offset,
    };

    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
