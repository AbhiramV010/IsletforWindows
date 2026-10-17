using System.Runtime.InteropServices;
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
            return SHQueryUserNotificationState(out int state) == 0 && state is Busy or RunningD3dFullScreen or PresentationMode;
        }
        catch (Exception)
        {
            return false;
        }
    }

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
