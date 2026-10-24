using System.Text;

namespace Fm4StaticRecomp.Launcher.Core.Setup;

// Setup's output mixes encodings: Python's forwarded events are UTF-8 while Windows
// PowerShell 5.1 writes the console code page (CP866 on a Russian PC). Each line is decoded
// on its own: as UTF-8 when its bytes are valid UTF-8, else in the console code page.
public static class ConsoleLines
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static string Decode(ReadOnlySpan<byte> line, Encoding fallback)
    {
        if (line.Length > 0 && line[^1] == (byte)'\r') line = line[..^1];
        try { return StrictUtf8.GetString(line); }
        catch (DecoderFallbackException) { return fallback.GetString(line); }
    }

    // A carriage return ends a line too: curl redraws its progress meter with one, and the
    // meter would otherwise arrive as one line when the download is done. The \n of a \r\n
    // ends nothing more.
    public static async Task PumpAsync(Stream stream, Encoding fallback, Action<string> onLine, CancellationToken cancellation)
    {
        var buffer = new byte[64 * 1024];
        var pending = new List<byte>();
        var afterReturn = false;
        int read;
        while ((read = await stream.ReadAsync(buffer, cancellation)) > 0)
        {
            var start = 0;
            for (var i = 0; i < read; i++)
            {
                var end = buffer[i] is (byte)'\r' or (byte)'\n';
                var lineFeedOfReturn = afterReturn && buffer[i] == (byte)'\n';
                afterReturn = buffer[i] == (byte)'\r';
                if (!end) continue;
                pending.AddRange(buffer.AsSpan(start, i - start));
                if (!lineFeedOfReturn) onLine(Decode(pending.ToArray(), fallback));
                pending.Clear();
                start = i + 1;
            }
            pending.AddRange(buffer.AsSpan(start, read - start));
        }
        if (pending.Count > 0) onLine(Decode(pending.ToArray(), fallback));
    }
}
