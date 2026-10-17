using System.Windows;
using System.Windows.Threading;
using Islet.App.Agents;
using Islet.App.Clock;
using Islet.App.Media;
using Islet.App.Interop;
using Islet.App.Notifications;
using Islet.App.Settings;
using Islet.App.Ui;
using Islet.App.ViewModels;

namespace Islet.App;

public partial class App : Application
{
    private OverlayWindow? _overlay;
    private DispatcherTimer? _demoTimer;
    private Mutex? _singleInstanceMutex;
    private MediaService? _media;
    private PipeServer? _pipeServer;
    private PermissionQueueViewModel? _permissions;
    private AgentSessionsViewModel? _sessions;
    private NotificationsViewModel? _notifications;
    private NotificationWatcher? _toasts;
    private ClockMirror? _clockMirror;
    private FullscreenWatcher? _fullscreen;
    private TrayController? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // A second copy would show a duplicate island and lose the hotkeys to the first.
        _singleInstanceMutex = new Mutex(initiallyOwned: true, IsletInfo.SingleInstanceMutexName, out bool createdNew);
        if (!createdNew)
        {
            FileLog.Write("Another instance is already running; exiting");
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;

        _media = new MediaService();
        var mediaViewModel = new MediaViewModel(_media);
        _media.SessionChanged += appId => Dispatcher.InvokeAsync(() => mediaViewModel.ApplySession(appId));
        _media.TrackChanged += track => Dispatcher.InvokeAsync(() => mediaViewModel.Apply(track));
        _media.PlaybackChanged += playback => Dispatcher.InvokeAsync(() => mediaViewModel.Apply(playback));

        _permissions = new PermissionQueueViewModel();
        _sessions = new AgentSessionsViewModel();
        _notifications = new NotificationsViewModel();
        var clock = new ClockViewModel();
        AppSettings settings = AppSettings.Load();
        _fullscreen = new FullscreenWatcher();
        _overlay = new OverlayWindow(
            new OverlayViewModel(mediaViewModel, _permissions, _sessions, clock, _notifications),
            settings,
            _fullscreen);
        _overlay.Show();
        _fullscreen.Start();

        _tray = new TrayController(settings, new StartupRegistration(), () => Dispatcher.BeginInvoke(Shutdown));

        _ = StartMediaAsync();

        _pipeServer = new PipeServer(IsletInfo.PipeName, new AgentHub(Dispatcher, _permissions, _sessions));
        _pipeServer.Start();

        _toasts = new NotificationWatcher(item => Dispatcher.InvokeAsync(() => _notifications.Enqueue(item)));
        _toasts.Start();

        _clockMirror = new ClockMirror(snapshot => Dispatcher.InvokeAsync(() => clock.Apply(snapshot)));
        _clockMirror.Start();

        if (e.Args.Contains("--demo", StringComparer.OrdinalIgnoreCase))
        {
            StartDemoCycle();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Waiting agents fall back to their own terminal prompt.
        _permissions?.CancelAll();
        _tray?.Dispose();
        _fullscreen?.Dispose();
        _clockMirror?.Dispose();
        _toasts?.Dispose();
        _pipeServer?.Dispose();
        _media?.Dispose();

        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }

    private async Task StartMediaAsync()
    {
        try
        {
            await _media!.StartAsync();
        }
        catch (Exception exception)
        {
            FileLog.Write("Media session manager unavailable", exception);
        }
    }

    /// <summary>Debug mode: cycle through the views on a timer, for eyeballing the animation.</summary>
    private void StartDemoCycle()
    {
        FileLog.Write("Demo mode: cycling views every 3 s");
        _demoTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _demoTimer.Tick += (_, _) => _overlay?.ViewModel.CycleForcedView();
        _demoTimer.Start();
    }

    private void OnDispatcherUnhandledException(object? sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // An overlay failure should not take the island down mid-session.
        FileLog.Write("Unhandled dispatcher exception", e.Exception);
        e.Handled = true;
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        FileLog.Write("Unobserved task exception", e.Exception);
        e.SetObserved();
    }

    private static void OnAppDomainUnhandledException(object? sender, UnhandledExceptionEventArgs e) =>
        FileLog.Write($"Unhandled exception (terminating={e.IsTerminating}): {e.ExceptionObject}");
}
