using System.Globalization;

namespace Fm4StaticRecomp.Launcher.Core.Install;

public static class SpaceCheck
{
    public const long Disc1 = 7_600_000_000;
    public const long Disc2 = 2_800_000_000;
    public const long Toolchain = 6_300_000_000;
    public const long Version = 3_100_000_000;
    public const long Margin = 2_000_000_000;

    public static long Needed(bool disc1, bool disc2, bool toolchain, bool version) =>
        (disc1 ? Disc1 : 0) + (disc2 ? Disc2 : 0) + (toolchain ? Toolchain : 0) + (version ? Version : 0) + Margin;

    public static string? Problem(string root, long needed, Func<string, long> freeBytes)
    {
        var free = freeBytes(root);
        return free >= needed
            ? null
            : string.Create(CultureInfo.InvariantCulture,
                $"The drive of {root} has too little free space: setup needs {needed / 1e9:0.0} GB, {free / 1e9:0.0} GB free.");
    }
}
