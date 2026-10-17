using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Islet.App.Interop;
using Islet.App.Notifications;
using Islet.App.Ui;
using Islet.App.ViewModels;
using Islet.Hook.Protocol;
using Microsoft.Win32;

namespace Islet.App;

/// <summary>
/// The island: an inverted trapezoid hanging from the top center of the primary
/// monitor, flush with the screen edge. Never activates (WS_EX_NOACTIVATE |
/// WS_EX_TOOLWINDOW plus WM_MOUSEACTIVATE), and animates between views by resizing
/// the shape inside a fixed-size transparent window.
/// </summary>
public partial class OverlayWindow : Window
{
    private const double TransitionMs = 250;
    private const double ContentFadeMs = 180;
    private const double ContentFadeDelayMs = 80;
    private const double TopmostReassertMs = 1000;
    private const double PulseMs = 900;

    /// <summary>Hover intent: brushing past the top edge on the way to a tab bar shouldn't expand it.</summary>
    private static readonly TimeSpan HoverEnterDelay = TimeSpan.FromMilliseconds(220);
    private static readonly TimeSpan HoverLeaveDelay = TimeSpan.FromMilliseconds(350);

    private const int HotkeyCycleViews = 1;
    private const int HotkeyQuit = 2;
    private const int HotkeyFakeRequest = 3;
    private const int HotkeyStopwatchToggle = 4;
    private const int HotkeyStopwatchReset = 5;
    private const int HotkeyAddAlarm = 6;
    private const int HotkeyDismissAlarm = 7;
    private const int HotkeyFakeNotification = 8;

    /// <summary>The alarm that Ctrl+Alt+A sets.</summary>
    private const int QuickAlarmMinutes = 15;
    private const int HotkeyAllow = 10;
    private const int HotkeyDeny = 11;

    private static readonly Dictionary<OverlayView, ViewMetrics> MetricsByView = new()
    {
        [OverlayView.Idle] = new ViewMetrics(220, 22, 12),
        [OverlayView.MediaCompact] = new ViewMetrics(340, 30, 16),
        [OverlayView.AgentCompact] = new ViewMetrics(340, 30, 16),
        [OverlayView.TimersCompact] = new ViewMetrics(340, 30, 16),
        [OverlayView.TimersExpanded] = new ViewMetrics(460, 61, 15),
        [OverlayView.Notification] = new ViewMetrics(460, 61, 15),
        [OverlayView.Alert] = new ViewMetrics(460, 73, 18),
        [OverlayView.MediaExpanded] = new ViewMetrics(420, 61, 15),
        [OverlayView.Permission] = new ViewMetrics(460, 73, 18),
    };

    private readonly Dictionary<OverlayView, FrameworkElement> _contentByView;
    private readonly DispatcherTimer _hoverTimer;

    /// <summary>Created in code: XAML brushes get frozen, and this one animates when a request is overdue.</summary>
    private readonly SolidColorBrush _borderBrush;
    private HwndSource? _hwndSource;
    private DispatcherTimer? _topmostWatchdog;
    private bool _pointerInside;
    private bool _cardKeysRegistered;
    private PermissionRequestViewModel? _watchedRequest;
    private readonly AlarmSound _alarmSound = new();
    private int _fakeRequestCount;

    public OverlayWindow(OverlayViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;

        _borderBrush = new SolidColorBrush((Color)FindResource("PillBorderColor"));
        Pill.BorderBrush = _borderBrush;
        DataContext = ViewModel;

        _contentByView = new Dictionary<OverlayView, FrameworkElement>
        {
            [OverlayView.Idle] = IdleContent,
            [OverlayView.MediaCompact] = MediaCompactContent,
            [OverlayView.AgentCompact] = AgentCompactContent,
            [OverlayView.TimersCompact] = TimersCompactContent,
            [OverlayView.TimersExpanded] = TimersExpandedContent,
            [OverlayView.Notification] = NotificationContent,
            [OverlayView.Alert] = AlertContent,
            [OverlayView.MediaExpanded] = MediaExpandedContent,
            [OverlayView.Permission] = PermissionContent,
        };

        _hoverTimer = new DispatcherTimer();
        _hoverTimer.Tick += OnHoverTimerTick;
        Pill.MouseEnter += OnPillMouseEnter;
        Pill.MouseLeave += OnPillMouseLeave;

        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        ViewModel.Permissions.PropertyChanged += OnPermissionsPropertyChanged;
        ViewModel.Timers.Alarms.PropertyChanged += OnAlarmsPropertyChanged;
        Reposition();
    }

    public OverlayViewModel ViewModel { get; }

    /// <summary>Centers the window on the primary monitor, touching its top edge.</summary>
    public void Reposition()
    {
        double screenWidth = SystemParameters.PrimaryScreenWidth;
        Left = Math.Max(0, (screenWidth - Width) / 2);
        Top = 0;
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
            $"{IsletInfo.Name} shown at ({Left:0},{Top:0}), screen {SystemParameters.PrimaryScreenWidth:0}x{SystemParameters.PrimaryScreenHeight:0}, " +
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
        _hoverTimer.Stop();

        IntPtr handle = _hwndSource?.Handle ?? IntPtr.Zero;
        if (handle != IntPtr.Zero)
        {
            Win32.UnregisterHotKey(handle, HotkeyCycleViews);
            Win32.UnregisterHotKey(handle, HotkeyQuit);
            Win32.UnregisterHotKey(handle, HotkeyFakeRequest);
            Win32.UnregisterHotKey(handle, HotkeyStopwatchToggle);
            Win32.UnregisterHotKey(handle, HotkeyStopwatchReset);
            Win32.UnregisterHotKey(handle, HotkeyAddAlarm);
            Win32.UnregisterHotKey(handle, HotkeyDismissAlarm);
            Win32.UnregisterHotKey(handle, HotkeyFakeNotification);
            SetCardKeys(false);
        }

        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        ViewModel.Permissions.PropertyChanged -= OnPermissionsPropertyChanged;
        ViewModel.Timers.Alarms.PropertyChanged -= OnAlarmsPropertyChanged;
        _alarmSound.Stop();
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
        const uint modifiers = Win32.MOD_CONTROL | Win32.MOD_ALT | Win32.MOD_NOREPEAT;
        bool cycle = Win32.RegisterHotKey(handle, HotkeyCycleViews, modifiers, Win32.VK_I);
        bool quit = Win32.RegisterHotKey(handle, HotkeyQuit, modifiers, Win32.VK_Q);
        bool fake = Win32.RegisterHotKey(handle, HotkeyFakeRequest, modifiers, Win32.VK_P);

        FileLog.Write($"Debug hotkeys registered: Ctrl+Alt+I={cycle}, Ctrl+Alt+Q={quit}, Ctrl+Alt+P={fake}");

        bool stopwatch = Win32.RegisterHotKey(handle, HotkeyStopwatchToggle, modifiers, Win32.VK_S);
        bool reset = Win32.RegisterHotKey(handle, HotkeyStopwatchReset, modifiers, Win32.VK_R);
        bool alarm = Win32.RegisterHotKey(handle, HotkeyAddAlarm, modifiers, Win32.VK_A);
        bool dismiss = Win32.RegisterHotKey(handle, HotkeyDismissAlarm, modifiers, Win32.VK_D);
        bool fakeNotification = Win32.RegisterHotKey(handle, HotkeyFakeNotification, modifiers, Win32.VK_N);
        FileLog.Write($"Debug notification hotkey: Ctrl+Alt+N={fakeNotification}");
        FileLog.Write(
            $"Timer hotkeys registered: Ctrl+Alt+S={stopwatch}, Ctrl+Alt+R={reset}, Ctrl+Alt+A={alarm}, Ctrl+Alt+D={dismiss}");
    }

    /// <summary>
    /// Keeps the island above every other always-on-top window. Windows stacks topmost
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
                handled = true;
                switch (wParam.ToInt32())
                {
                    case HotkeyCycleViews:
                        ViewModel.CycleForcedView();
                        break;
                    case HotkeyQuit:
                        Dispatcher.BeginInvoke(new Action(Close));
                        break;
                    case HotkeyFakeRequest:
                        EnqueueFakeRequest();
                        break;
                    case HotkeyStopwatchToggle:
                        ViewModel.Timers.Stopwatch.Toggle();
                        break;
                    case HotkeyStopwatchReset:
                        ViewModel.Timers.Stopwatch.Reset();
                        break;
                    case HotkeyAddAlarm:
                        ViewModel.Timers.AddAlarm(QuickAlarmMinutes);
                        FileLog.Write($"Alarm set for {QuickAlarmMinutes} min");
                        break;
                    case HotkeyDismissAlarm:
                        ViewModel.Timers.Alarms.DismissOrClear();
                        break;
                    case HotkeyFakeNotification:
                        ShowFakeNotification();
                        break;
                    case HotkeyAllow:
                        Decide(allow: true, "Enter");
                        break;
                    case HotkeyDeny:
                        Decide(allow: false, "Esc");
                        break;
                    default:
                        handled = false;
                        break;
                }

                break;
        }

        return IntPtr.Zero;
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) =>
        Dispatcher.InvokeAsync(Reposition);

    // ---- Hover ---------------------------------------------------------------------------

    private void OnPillMouseEnter(object sender, MouseEventArgs e)
    {
        _pointerInside = true;
        RestartHoverTimer(HoverEnterDelay);
        UpdateCardKeys();
    }

    private void OnPillMouseLeave(object sender, MouseEventArgs e)
    {
        _pointerInside = false;
        RestartHoverTimer(HoverLeaveDelay);
        UpdateCardKeys();
    }

    private void RestartHoverTimer(TimeSpan delay)
    {
        _hoverTimer.Stop();
        _hoverTimer.Interval = delay;
        _hoverTimer.Start();
    }

    private void OnHoverTimerTick(object? sender, EventArgs e)
    {
        _hoverTimer.Stop();
        ViewModel.IsHovered = _pointerInside;
    }

    /// <summary>
    /// Enter and Esc answer the card only while the pointer is on it. The island never has
    /// keyboard focus, so they are registered as hotkeys for exactly that window of time.
    /// </summary>
    private void UpdateCardKeys() =>
        SetCardKeys(_pointerInside &&
            ViewModel.View == OverlayView.Permission &&
            ViewModel.Permissions.Current is { IsInteractive: true });

    private void SetCardKeys(bool register)
    {
        if (_hwndSource is null || register == _cardKeysRegistered)
        {
            return;
        }

        IntPtr handle = _hwndSource.Handle;
        if (register)
        {
            bool enter = Win32.RegisterHotKey(handle, HotkeyAllow, Win32.MOD_NOREPEAT, Win32.VK_RETURN);
            bool escape = Win32.RegisterHotKey(handle, HotkeyDeny, Win32.MOD_NOREPEAT, Win32.VK_ESCAPE);
            FileLog.Write($"Card keys on: Enter={enter}, Esc={escape}");
        }
        else
        {
            Win32.UnregisterHotKey(handle, HotkeyAllow);
            Win32.UnregisterHotKey(handle, HotkeyDeny);
            FileLog.Write("Card keys off");
        }

        _cardKeysRegistered = register;
    }

    // ---- Buttons -------------------------------------------------------------------------

    private void OnAllowClick(object sender, RoutedEventArgs e) => Decide(allow: true, "click");

    private void OnDenyClick(object sender, RoutedEventArgs e) => Decide(allow: false, "click");

    private void Decide(bool allow, string source)
    {
        PermissionQueueViewModel queue = ViewModel.Permissions;
        string tool = queue.Current?.Tool ?? "";
        bool accepted = allow ? queue.Allow() : queue.Deny();
        FileLog.Write(accepted
            ? $"Permission {(allow ? "allowed" : "denied")} by {source}: {tool}"
            : $"Permission {(allow ? "allow" : "deny")} by {source} ignored (no card, or card just appeared)");
    }

    private void OnShowTimersClick(object sender, RoutedEventArgs e) => ViewModel.ShowTimersPanel();

    private void OnShowMediaClick(object sender, RoutedEventArgs e) => ViewModel.ShowMediaPanel();

    private void OnStopwatchToggleClick(object sender, RoutedEventArgs e) => ViewModel.Timers.Stopwatch.Toggle();

    private void OnStopwatchResetClick(object sender, RoutedEventArgs e) => ViewModel.Timers.Stopwatch.Reset();

    private void OnAddAlarmClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string tag } && int.TryParse(tag, out int minutes))
        {
            ViewModel.Timers.AddAlarm(minutes);
            FileLog.Write($"Alarm set for {minutes} min");
        }
    }

    private void OnClearAlarmsClick(object sender, RoutedEventArgs e) => ViewModel.Timers.Alarms.DismissOrClear();

    private void OnSnoozeClick(object sender, RoutedEventArgs e) => ViewModel.Timers.Alarms.Snooze();

    private void OnDismissAlarmClick(object sender, RoutedEventArgs e) => ViewModel.Timers.Alarms.Dismiss();

    private void OnNotificationClick(object sender, MouseButtonEventArgs e) => ViewModel.Notifications.Dismiss();

    /// <summary>Debug: shows a fake notification, so the banner can be tried without a real toast.</summary>
    internal void ShowFakeNotification() =>
        ViewModel.Notifications.Enqueue(new NotificationItem("Debug", "Islet test", "A fake notification, " + DateTime.Now.ToString("HH:mm:ss")));

    private void OnPlayPauseClick(object sender, RoutedEventArgs e) => ViewModel.Media.TogglePlayPause();

    private void OnNextClick(object sender, RoutedEventArgs e) => ViewModel.Media.Next();

    private void OnPreviousClick(object sender, RoutedEventArgs e) => ViewModel.Media.Previous();

    /// <summary>Debug: a local card with no agent behind it, for testing the UI without hooks.</summary>
    private void EnqueueFakeRequest()
    {
        _fakeRequestCount++;
        using var input = System.Text.Json.JsonDocument.Parse(
            $$"""{"command":"npm test -- --grep \"fake request {{_fakeRequestCount}}\"","description":"Debug request"}""");
        PermissionRequestViewModel request = ViewModel.Permissions.Enqueue(new HookMessage
        {
            Id = $"debug-{_fakeRequestCount}",
            Agent = "claude",
            Event = PipeProtocol.PermissionRequestEvent,
            Cwd = @"C:\dev\islet-debug",
            Tool = "Bash",
            Input = input.RootElement.Clone(),
        });
        request.Reply.ContinueWith(
            task => FileLog.Write($"Debug request resolved: {task.Result.Decision}"),
            TaskScheduler.Default);
    }

    // ---- View changes --------------------------------------------------------------------

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OverlayViewModel.View))
        {
            AnimateTo(ViewModel.View);
            UpdateCardKeys();
        }
    }

    private void OnPermissionsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(PermissionQueueViewModel.Current))
        {
            return;
        }

        if (_watchedRequest is not null)
        {
            _watchedRequest.PropertyChanged -= OnRequestPropertyChanged;
        }

        _watchedRequest = ViewModel.Permissions.Current;
        if (_watchedRequest is not null)
        {
            _watchedRequest.PropertyChanged += OnRequestPropertyChanged;

            // Fade the next card in, even though the view itself stays on Permission.
            FadeIn(PermissionContent);
        }

        // A question card has no Allow / Deny, so Enter and Esc must go back to the terminal.
        UpdateCardKeys();

        UpdatePulse();
    }

    private void OnAlarmsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(Timers.AlarmsViewModel.Ringing))
        {
            return;
        }

        if (ViewModel.Timers.Alarms.Ringing is not null)
        {
            _alarmSound.Start();
        }
        else
        {
            _alarmSound.Stop();
        }

        UpdatePulse();
    }

    private void OnRequestPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PermissionRequestViewModel.IsOverdue))
        {
            UpdatePulse();
        }
    }

    /// <summary>A request unanswered for 30 s, or a ringing alarm, gets a pulsing border.</summary>
    private void UpdatePulse()
    {
        if (ViewModel.Permissions.Current is { IsOverdue: true } || ViewModel.Timers.Alarms.Ringing is not null)
        {
            if (_borderBrush.HasAnimatedProperties)
            {
                return;
            }

            _borderBrush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation
            {
                From = (Color)FindResource("PillBorderColor"),
                To = (Color)FindResource("PulseColor"),
                Duration = TimeSpan.FromMilliseconds(PulseMs),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            });
            Pill.BorderThickness = 1.5;
        }
        else
        {
            _borderBrush.BeginAnimation(SolidColorBrush.ColorProperty, null);
            Pill.BorderThickness = 1;
        }
    }

    private void AnimateTo(OverlayView view)
    {
        ViewMetrics metrics = MetricsByView[view];
        var duration = TimeSpan.FromMilliseconds(TransitionMs);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        var storyboard = new Storyboard();
        storyboard.Children.Add(CreateAnimation(Pill, WidthProperty, Pill.Width, metrics.Width, duration, ease));
        storyboard.Children.Add(CreateAnimation(Pill, HeightProperty, Pill.Height, metrics.Height, duration, ease));
        storyboard.Children.Add(CreateAnimation(
            Pill, TrapezoidBorder.BottomInsetProperty, Pill.BottomInset, metrics.BottomInset, duration, ease));
        storyboard.Begin();

        foreach ((OverlayView candidate, FrameworkElement content) in _contentByView)
        {
            content.Visibility = candidate == view ? Visibility.Visible : Visibility.Collapsed;
        }

        FadeIn(_contentByView[view]);
    }

    private static void FadeIn(FrameworkElement content)
    {
        content.BeginAnimation(OpacityProperty, null);
        content.Opacity = 0;
        content.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(ContentFadeMs))
            {
                BeginTime = TimeSpan.FromMilliseconds(ContentFadeDelayMs),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.HoldEnd,
            });
    }

    private static DoubleAnimation CreateAnimation(
        DependencyObject target, DependencyProperty property, double from, double to, TimeSpan duration, IEasingFunction ease)
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

    private readonly record struct ViewMetrics(double Width, double Height, double BottomInset);
}
