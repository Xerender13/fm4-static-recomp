using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace Fm4StaticRecomp.Launcher;

// The refresh rate and size of the display a window is on, as the game reads the rate
// (src\ui\graphics_page.cpp DisplayRefreshHz); null when they cannot be read (remote desktop).
public static class DisplayInfo
{
    private const int DefaultToNearest = 2;
    private const int CurrentSettings = -1;

    public static int? RefreshHz(TopLevel? window) => Mode(window) is { DisplayFrequency: > 1 } mode ? mode.DisplayFrequency : null;

    // The display mode's size, which the game's fullscreen output takes.
    public static (int Width, int Height)? Size(TopLevel? window) =>
        Mode(window) is { PelsWidth: > 0, PelsHeight: > 0 } mode ? (mode.PelsWidth, mode.PelsHeight) : null;

    private static DevMode? Mode(TopLevel? window)
    {
        if (!OperatingSystem.IsWindows() || window?.TryGetPlatformHandle()?.Handle is not { } handle) return null;
        var monitor = new MonitorInfoEx { Size = Marshal.SizeOf<MonitorInfoEx>() };
        if (!GetMonitorInfo(MonitorFromWindow(handle, DefaultToNearest), ref monitor)) return null;
        var mode = new DevMode { Size = (short)Marshal.SizeOf<DevMode>() };
        return EnumDisplaySettings(monitor.DeviceName, CurrentSettings, ref mode) ? mode : null;
    }

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint window, int flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfoEx info);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "EnumDisplaySettingsW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplaySettings(string deviceName, int modeNumber, ref DevMode mode);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int Size;
        public Rect Monitor, Work;
        public int Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DevMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        public short SpecVersion, DriverVersion, Size, DriverExtra;
        public int Fields, PositionX, PositionY, DisplayOrientation, DisplayFixedOutput;
        public short Color, Duplex, YResolution, TTOption, Collate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string FormName;
        public short LogPixels;
        public int BitsPerPel, PelsWidth, PelsHeight, DisplayFlags, DisplayFrequency;
        public int IcmMethod, IcmIntent, MediaType, DitherType, Reserved1, Reserved2, PanningWidth, PanningHeight;
    }
}
