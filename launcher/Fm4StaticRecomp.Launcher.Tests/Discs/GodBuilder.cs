using System.Buffers.Binary;
using System.Text;

namespace Fm4StaticRecomp.Launcher.Tests.Discs;

public static class GodBuilder
{
    public const int Block = 0x1000;
    public const int BlocksPerSubpart = 204;
    public const int SubpartsPerFile = 203;

    public static byte[] Header(string magic, uint contentType, uint titleId, uint mediaId, string displayName)
    {
        var header = new byte[0xB000];
        Encoding.ASCII.GetBytes(magic).CopyTo(header, 0);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0x344), contentType);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0x354), mediaId);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0x360), titleId);
        Encoding.BigEndianUnicode.GetBytes(displayName).CopyTo(header, 0x411);
        return header;
    }

    // Writes <root>/4D530910/00007000/<name> and <name>.data/Data0000... holding `data` with
    // zeroed hash blocks; maxBlocksPerFile shrinks files so tests cross a file boundary cheaply.
    public static string Write(string root, byte[] data, int maxSubpartsPerFile = SubpartsPerFile, uint mediaId = 0x61D62D9F)
    {
        var folder = Path.Combine(root, "4D530910", "00007000");
        Directory.CreateDirectory(folder);
        var headerPath = Path.Combine(folder, "ABCDEF0123456789");
        File.WriteAllBytes(headerPath, Header("LIVE", 0x7000, 0x4D530910, mediaId, "Forza Motorsport 4"));
        var dataFolder = headerPath + ".data";
        Directory.CreateDirectory(dataFolder);
        var blocks = (data.Length + Block - 1) / Block;
        var perFile = maxSubpartsPerFile * BlocksPerSubpart;
        for (int file = 0, first = 0; first < blocks; file++, first += perFile)
        {
            using var output = File.Create(Path.Combine(dataFolder, $"Data{file:D4}"));
            output.Write(new byte[Block]);
            for (var b = 0; b < Math.Min(perFile, blocks - first); b++)
            {
                if (b % BlocksPerSubpart == 0) output.Write(new byte[Block]);
                var chunk = new byte[Block];
                var start = (first + b) * Block;
                Array.Copy(data, start, chunk, 0, Math.Min(Block, data.Length - start));
                output.Write(chunk);
            }
        }
        return root;
    }
}
