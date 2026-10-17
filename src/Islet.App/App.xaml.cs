using System.Windows;
using System.Windows.Threading;

namespace Islet.App;

public partial class App : Application
{
    private OverlayWindow? _overlay;
    private DispatcherTimer? _demoTimer;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;

        _overlay = new OverlayWindow();
        _overlay.Show();

        if (e.Args.Contains("--demo", StringComparer.OrdinalIgnoreCase))
        {
            StartDemoCycle();
        }
    }

    /// <summary>Debug mode: cycle through the states on a timer, for eyeballing the animation.</summary>
    private void StartDemoCycle()
    {
        FileLog.Write("Demo mode: cycling states every 3 s");
        _demoTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _demoTimer.Tick += (_, _) => _overlay?.ViewModel.CycleState();
        _demoTimer.Start();
    }

    private void OnDispatcherUnhandledException(object? sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // An overlay failure should not take the pill down mid-session.
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
