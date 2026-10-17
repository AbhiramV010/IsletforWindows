using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Islet.App.Interop;
using Islet.App.Ui;
using Islet.App.ViewModels;
using Microsoft.Win32;

namespace Islet.App;

/// <summary>
/// The pill. Sits at the top center of the primary monitor's work area, never
/// activates (WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW plus WM_MOUSEACTIVATE), and
/// animates between states by resizing the pill container inside a fixed,
/// fixed-size transparent window.
/// </summary>
public partial class OverlayWindow : Window
{
    private const double TransitionMs = 250;
    private const double ContentFadeMs = 180;
    private const double ContentFadeDelayMs = 80;
    private const double TopmostReassertMs = 1000;

    private const int HotkeyCycleStates = 1;
    private const int HotkeyQuit = 2;

    private static readonly Dictionary<OverlayState, StateMetrics> MetricsByState = new()
    {
        [OverlayState.Collapsed] = new StateMetrics(200, 36, 18),
        [OverlayState.Compact] = new StateMetrics(280, 44, 22),
        [OverlayState.Expanded] = new StateMetrics(380, 132, 24),
    };

    private readonly Dictionary<OverlayState, FrameworkElement> _contentByState;
    private HwndSource? _hwndSource;
    private DispatcherTimer? _topmostWatchdog;

    public OverlayWindow()
    {
        InitializeComponent();
        DataContext = ViewModel;

        _contentByState = new Dictionary<OverlayState, FrameworkElement>
        {
            [OverlayState.Collapsed] = CollapsedContent,
            [OverlayState.Compact] = CompactContent,
            [OverlayState.Expanded] = ExpandedContent,
        };

        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        Reposition();
    }

    public OverlayViewModel ViewModel { get; } = new();

    /// <summary>Centers the window on the primary monitor's work area.</summary>
    public void Reposition()
    {
        Rect workArea = SystemParameters.WorkArea;
        Left = workArea.Left + Math.Max(0, (workArea.Width - Width) / 2);
        Top = workArea.Top;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        _hwndSource = (HwndSource)PresentationSource.FromVisual(this)!;
        ApplyNoActivateStyles();
        _hwndSource.AddHook(WndProc);
        RegisterDebugHotkeys();
        StartTopmostWatchdog();

        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        FileLog.Write(
            $"{IsletInfo.Name} shown at ({Left:0},{Top:0}), work area {SystemParameters.WorkArea}, " +
            $"primary DPI {VisualTreeHelper.GetDpi(this).PixelsPerDip * 96:0}");
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        Reposition();
    }

    protected override void OnClosed(EventArgs e)
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;

        _topmostWatchdog?.Stop();
        _topmostWatchdog = null;

        IntPtr handle = _hwndSource?.Handle ?? IntPtr.Zero;
        if (handle != IntPtr.Zero)
        {
            Win32.UnregisterHotKey(handle, HotkeyCycleStates);
            Win32.UnregisterHotKey(handle, HotkeyQuit);
        }

        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _hwndSource?.RemoveHook(WndProc);
        _hwndSource = null;

        base.OnClosed(e);
    }

    private void ApplyNoActivateStyles()
    {
        IntPtr handle = _hwndSource!.Handle;
        long exStyle = Win32.GetWindowLongPtr(handle, Win32.GWL_EXSTYLE);
        exStyle |= Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOACTIVATE;
        Win32.SetWindowLongPtr(handle, Win32.GWL_EXSTYLE, exStyle);

        // Read back so the log shows what the OS actually has, not what we asked for.
        long applied = Win32.GetWindowLongPtr(handle, Win32.GWL_EXSTYLE);
        FileLog.Write(
            $"Window styles: exStyle=0x{applied:X8} toolWindow={HasStyle(applied, Win32.WS_EX_TOOLWINDOW)} " +
            $"noActivate={HasStyle(applied, Win32.WS_EX_NOACTIVATE)}");

        static bool HasStyle(long value, int style) => (value & style) == style;
    }

    private void RegisterDebugHotkeys()
    {
        IntPtr handle = _hwndSource!.Handle;
        bool cycle = Win32.RegisterHotKey(
            handle, HotkeyCycleStates, Win32.MOD_CONTROL | Win32.MOD_ALT | Win32.MOD_NOREPEAT, Win32.VK_I);
        bool quit = Win32.RegisterHotKey(
            handle, HotkeyQuit, Win32.MOD_CONTROL | Win32.MOD_ALT | Win32.MOD_NOREPEAT, Win32.VK_Q);

        FileLog.Write($"Debug hotkeys registered: Ctrl+Alt+I={cycle}, Ctrl+Alt+Q={quit}");
    }

    /// <summary>
    /// Keeps the pill above every other always-on-top window. Windows stacks topmost
    /// windows in activation order, so a topmost window created later (a notification, an
    /// installer, another overlay) would otherwise sit above us. Re-asserting uses
    /// SWP_NOACTIVATE with no move/resize, so it is invisible and cannot steal focus.
    /// </summary>
    private void StartTopmostWatchdog()
    {
        AssertTopmost();

        _topmostWatchdog = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(TopmostReassertMs),
        };
        _topmostWatchdog.Tick += (_, _) => AssertTopmost();
        _topmostWatchdog.Start();
    }

    private void AssertTopmost()
    {
        if (_hwndSource is null)
        {
            return;
        }

        Win32.SetWindowPos(
            _hwndSource.Handle,
            Win32.HWND_TOPMOST,
            0,
            0,
            0,
            0,
            Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case Win32.WM_MOUSEACTIVATE:
                // Never steal focus from the terminal or a game, even on click.
                handled = true;
                return new IntPtr(Win32.MA_NOACTIVATE);

            case Win32.WM_HOTKEY:
                switch (wParam.ToInt32())
                {
                    case HotkeyCycleStates:
                        ViewModel.CycleState();
                        handled = true;
                        break;
                    case HotkeyQuit:
                        Dispatcher.BeginInvoke(new Action(Close));
                        handled = true;
                        break;
                }

                break;
        }

        return IntPtr.Zero;
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) =>
        Dispatcher.InvokeAsync(Reposition);

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OverlayViewModel.State))
        {
            AnimateTo(ViewModel.State);
        }
    }

    private void AnimateTo(OverlayState state)
    {
        StateMetrics metrics = MetricsByState[state];
        var duration = TimeSpan.FromMilliseconds(TransitionMs);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        var storyboard = new Storyboard();
        storyboard.Children.Add(CreateAnimation(Pill, "Width", Pill.Width, metrics.Width, duration, ease));
        storyboard.Children.Add(CreateAnimation(Pill, "Height", Pill.Height, metrics.Height, duration, ease));
        storyboard.Children.Add(CreateCornerRadiusAnimation(
            Pill, Pill.CornerRadius, new CornerRadius(metrics.Radius), duration, ease));
        storyboard.Begin();

        FrameworkElement content = _contentByState[state];
        content.BeginAnimation(OpacityProperty, null);
        content.Opacity = 0;
        content.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(ContentFadeMs))
            {
                BeginTime = TimeSpan.FromMilliseconds(ContentFadeDelayMs),
                EasingFunction = ease,
                FillBehavior = FillBehavior.HoldEnd,
            });
    }

    private static DoubleAnimation CreateAnimation(
        DependencyObject target, string property, double from, double to, TimeSpan duration, IEasingFunction ease)
    {
        var animation = new DoubleAnimation(from, to, duration)
        {
            EasingFunction = ease,
            FillBehavior = FillBehavior.HoldEnd,
        };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, new PropertyPath(property));
        return animation;
    }

    private static CornerRadiusAnimation CreateCornerRadiusAnimation(
        DependencyObject target, CornerRadius from, CornerRadius to, TimeSpan duration, IEasingFunction ease)
    {
        var animation = new CornerRadiusAnimation
        {
            From = from,
            To = to,
            Duration = duration,
            EasingFunction = ease,
            FillBehavior = FillBehavior.HoldEnd,
        };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, new PropertyPath(nameof(Border.CornerRadius)));
        return animation;
    }

    private readonly record struct StateMetrics(double Width, double Height, double Radius);
}
