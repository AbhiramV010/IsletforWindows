using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Threading;

namespace Islet.App.Interop;

/// <summary>
/// Tells when a fullscreen app (a game, a fullscreen video, a presentation) is in front. It asks
/// Windows through <c>SHQueryUserNotificationState</c>, the same answer the shell uses to hold
/// back notifications, and re-asks only when the foreground window changes or moves. Event
/// driven; no polling.
/// </summary>
public sealed class FullscreenWatcher : IDisposable
{
    private const uint EventSystemForeground = 0x0003;
    private const uint EventObjectLocationChange = 0x800B;
    private const uint WinEventOutOfContext = 0x0000;
    private const uint WinEventSkipOwnProcess = 0x0002;
    private const int ObjIdWindow = 0;

    /// <summary>States where the shell treats the user as busy with a fullscreen app.</summary>
    private const int Busy = 2;
    private const int RunningD3dFullScreen = 3;
    private const int PresentationMode = 4;

    // A moving or resizing window fires location events constantly. One check after it settles is enough.
    private readonly DispatcherTimer _debounce = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private readonly WinEventProc _callback;
    private IntPtr _foregroundHook;
    private IntPtr _locationHook;
    private bool _isFullscreen;

    public FullscreenWatcher()
    {
        _callback = OnWinEvent;
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            Evaluate();
        };
    }

    /// <summary>Raised on the UI thread when a fullscreen app comes to the front or leaves.</summary>
    public event Action<bool>? Changed;

    public bool IsFullscreen => _isFullscreen;

    public void Start()
    {
        _foregroundHook = SetWinEventHook(
            EventSystemForeground, EventSystemForeground, IntPtr.Zero, _callback, 0, 0, WinEventOutOfContext | WinEventSkipOwnProcess);
        _locationHook = SetWinEventHook(
            EventObjectLocationChange, EventObjectLocationChange, IntPtr.Zero, _callback, 0, 0, WinEventOutOfContext | WinEventSkipOwnProcess);

        Evaluate();
    }

    public void Dispose()
    {
        _debounce.Stop();
        if (_foregroundHook != IntPtr.Zero)
        {
            UnhookWinEvent(_foregroundHook);
            _foregroundHook = IntPtr.Zero;
        }

        if (_locationHook != IntPtr.Zero)
        {
            UnhookWinEvent(_locationHook);
            _locationHook = IntPtr.Zero;
        }
    }

    private void OnWinEvent(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (eventType == EventObjectLocationChange && (idObject != ObjIdWindow || hwnd != GetForegroundWindow()))
        {
            return;
        }

        _debounce.Stop();
        _debounce.Start();
    }

    private void Evaluate()
    {
        bool fullscreen = QueryFullscreen();
        if (fullscreen == _isFullscreen)
        {
            return;
        }

        _isFullscreen = fullscreen;
        Changed?.Invoke(fullscreen);
    }

    private static bool QueryFullscreen()
    {
        try
        {
            if (SHQueryUserNotificationState(out int state) != 0)
            {
                return false;
            }

            // Busy also fires for any topmost window covering the screen, including click-through
            // overlays like screen dimmers. Only trust it when the app in front fills its monitor.
            return state is RunningD3dFullScreen or PresentationMode || (state == Busy && ForegroundFillsMonitor());
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool ForegroundFillsMonitor()
    {
        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero || IsDesktop(hwnd) || IsClickThrough(hwnd) || !GetWindowRect(hwnd, out Rect window))
        {
            return false;
        }

        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromWindow(hwnd, MonitorDefaultToNearest), ref info))
        {
            return false;
        }

        Rect monitor = info.Monitor;
        return window.Left <= monitor.Left && window.Top <= monitor.Top &&
               window.Right >= monitor.Right && window.Bottom >= monitor.Bottom;
    }

    /// <summary>The desktop covers the monitor too, but clicking it is not a fullscreen app.</summary>
    private static bool IsDesktop(IntPtr hwnd)
    {
        if (hwnd == GetShellWindow())
        {
            return true;
        }

        var name = new StringBuilder(32);
        GetClassName(hwnd, name, name.Capacity);
        return name.ToString() is "Progman" or "WorkerW";
    }

    /// <summary>
    /// A layered window that lets clicks through is an overlay (a screen dimmer's tint), not an
    /// app. Dimmers like Nelson Pires' Dimmer don't mark their tint no-activate, so Windows hands
    /// it the foreground when their control window closes, and it covers the whole monitor.
    /// </summary>
    private static bool IsClickThrough(IntPtr hwnd)
    {
        long exStyle = Win32.GetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE);
        return (exStyle & (WsExLayered | WsExTransparent)) == (WsExLayered | WsExTransparent);
    }

    private const long WsExTransparent = 0x00000020;
    private const long WsExLayered = 0x00080000;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
    }

    private const uint MonitorDefaultToNearest = 2;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    private static extern IntPtr GetShellWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetClassNameW")]
    private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int capacity);

    private delegate void WinEventProc(IntPtr hook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

    [DllImport("user32.dll")]
    private static extern IntPtr SetWinEventHook(
        uint eventMin, uint eventMax, IntPtr module, WinEventProc callback, uint process, uint thread, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWinEvent(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out int state);
}
