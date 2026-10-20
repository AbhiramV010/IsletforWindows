using System.Runtime.InteropServices;

namespace Islet.App.Interop;

/// <summary>A monitor in physical pixels.</summary>
public readonly record struct MonitorInfo(string DeviceName, int Left, int Top, int Width, int Height, bool IsPrimary, uint Dpi)
{
    /// <summary>"Display 2 (1920×1080)" style label for the tray menu.</summary>
    public string Label(int number) => $"Display {number} ({Width}×{Height}){(IsPrimary ? " · primary" : "")}";
}

/// <summary>Monitor lookup and the island's position on one.</summary>
public static class Monitors
{
    private const int MonitorInfoPrimary = 1;
    private const int MdtEffectiveDpi = 0;
    private const uint DefaultDpi = 96;
    private const int VRefresh = 116;
    private const int DefaultRefreshRate = 60;

    /// <summary>All attached monitors, primary first.</summary>
    public static IReadOnlyList<MonitorInfo> List()
    {
        var found = new List<MonitorInfo>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr monitor, IntPtr hdc, ref Rect rect, IntPtr data) =>
        {
            var info = new MonitorInfoEx { Size = Marshal.SizeOf<MonitorInfoEx>() };
            if (GetMonitorInfo(monitor, ref info))
            {
                uint dpi = GetDpiForMonitor(monitor, MdtEffectiveDpi, out uint dpiX, out _) == 0 ? dpiX : DefaultDpi;
                found.Add(new MonitorInfo(
                    info.Device,
                    info.Monitor.Left,
                    info.Monitor.Top,
                    info.Monitor.Right - info.Monitor.Left,
                    info.Monitor.Bottom - info.Monitor.Top,
                    (info.Flags & MonitorInfoPrimary) != 0,
                    dpi));
            }

            return true;
        }, IntPtr.Zero);

        return found.OrderByDescending(monitor => monitor.IsPrimary).ToList();
    }

    /// <summary>
    /// The monitor named by <paramref name="device"/>, or the primary one when none is named or the
    /// named one isn't attached (a laptop undocked, say). Null only if no monitors are reported.
    /// </summary>
    public static MonitorInfo? Choose(IReadOnlyList<MonitorInfo> monitors, string? device)
    {
        if (device is not null)
        {
            foreach (MonitorInfo monitor in monitors)
            {
                if (string.Equals(monitor.DeviceName, device, StringComparison.OrdinalIgnoreCase))
                {
                    return monitor;
                }
            }
        }

        foreach (MonitorInfo monitor in monitors)
        {
            if (monitor.IsPrimary)
            {
                return monitor;
            }
        }

        return monitors.Count > 0 ? monitors[0] : null;
    }

    /// <summary>
    /// Where the island's top-left corner goes, in physical pixels: horizontally centred on the
    /// monitor and touching its top edge. The width is in device-independent units, so it is
    /// scaled by that monitor's DPI.
    /// </summary>
    public static (int X, int Y) TopCenter(MonitorInfo monitor, double widthDips)
    {
        double scale = monitor.Dpi / (double)DefaultDpi;
        int widthPx = (int)Math.Round(widthDips * scale);
        return (monitor.Left + ((monitor.Width - widthPx) / 2), monitor.Top);
    }

    /// <summary>The monitor's refresh rate in Hz, or 60 when Windows won't say.</summary>
    public static int RefreshRate(MonitorInfo monitor)
    {
        IntPtr hdc = CreateDC(null, monitor.DeviceName, null, IntPtr.Zero);
        if (hdc == IntPtr.Zero)
        {
            return DefaultRefreshRate;
        }

        try
        {
            // 0 and 1 mean "the hardware default", which says nothing about the rate.
            int rate = GetDeviceCaps(hdc, VRefresh);
            return rate > 1 ? rate : DefaultRefreshRate;
        }
        finally
        {
            DeleteDC(hdc);
        }
    }

    private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, ref Rect rect, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public int Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string Device;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfoEx info);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateDC(string? driver, string device, string? output, IntPtr devMode);

    [DllImport("gdi32.dll")]
    private static extern int GetDeviceCaps(IntPtr hdc, int index);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);
}
