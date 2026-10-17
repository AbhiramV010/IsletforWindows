using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Hardcodet.Wpf.TaskbarNotification;
using Islet.App.Interop;
using Islet.App.Settings;

namespace Islet.App.Ui;

/// <summary>
/// The notification-area icon and its menu, which is also where the settings live: start with
/// Windows, hide in fullscreen apps, which monitor to sit on, the logs folder, and quit.
/// </summary>
public sealed class TrayController : IDisposable
{
    private readonly AppSettings _settings;
    private readonly StartupRegistration _startup;
    private readonly Action _quit;
    private readonly TaskbarIcon _icon = new();
    private readonly MenuItem _startupItem;
    private readonly MenuItem _hideItem;
    private readonly MenuItem _monitorItem;

    public TrayController(AppSettings settings, StartupRegistration startup, Action quit)
    {
        _settings = settings;
        _startup = startup;
        _quit = quit;

        _startupItem = new MenuItem { Header = "Start with Windows", IsCheckable = true };
        _startupItem.Click += OnStartupClick;

        _hideItem = new MenuItem { Header = "Hide in fullscreen apps", IsCheckable = true };
        _hideItem.Click += (_, _) => _settings.SetHideInFullscreen(_hideItem.IsChecked);

        _monitorItem = new MenuItem { Header = "Show on monitor" };

        var logs = new MenuItem { Header = "Open logs folder" };
        logs.Click += (_, _) => OpenLogs();

        var quitItem = new MenuItem { Header = $"Quit {IsletInfo.Name}" };
        quitItem.Click += (_, _) => _quit();

        var menu = new ContextMenu();
        menu.Items.Add(new MenuItem { Header = IsletInfo.Name, IsEnabled = false, FontWeight = FontWeights.SemiBold });
        menu.Items.Add(new Separator());
        menu.Items.Add(_startupItem);
        menu.Items.Add(_hideItem);
        menu.Items.Add(_monitorItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(logs);
        menu.Items.Add(quitItem);
        menu.Opened += (_, _) => Refresh();

        _icon.ToolTipText = IsletInfo.Name;
        _icon.IconSource = TrayIconImage.Create();
        _icon.ContextMenu = menu;
    }

    public void Dispose() => _icon.Dispose();

    /// <summary>Brings the checkmarks and the monitor list up to date each time the menu opens.</summary>
    private void Refresh()
    {
        _startupItem.IsChecked = _startup.IsEnabled;
        _hideItem.IsChecked = _settings.HideInFullscreen;

        _monitorItem.Items.Clear();
        _monitorItem.Items.Add(MonitorChoice("Primary monitor", device: null));

        IReadOnlyList<MonitorInfo> monitors = Monitors.List();
        for (int i = 0; i < monitors.Count; i++)
        {
            _monitorItem.Items.Add(MonitorChoice(monitors[i].Label(i + 1), monitors[i].DeviceName));
        }
    }

    private MenuItem MonitorChoice(string label, string? device)
    {
        var item = new MenuItem
        {
            Header = label,
            IsCheckable = true,
            IsChecked = string.Equals(_settings.MonitorDevice, device, StringComparison.OrdinalIgnoreCase),
        };
        item.Click += (_, _) => _settings.SetMonitorDevice(device);
        return item;
    }

    private void OnStartupClick(object sender, RoutedEventArgs e)
    {
        bool wanted = _startupItem.IsChecked;
        if (!_startup.Set(wanted))
        {
            // The registry write failed; show what is really set.
            _startupItem.IsChecked = _startup.IsEnabled;
        }
    }

    private static void OpenLogs()
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", Hook.ProductInfo.LogDirectory) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception exception)
        {
            FileLog.Write("Could not open the logs folder", exception);
        }
    }
}

/// <summary>The tray glyph: the island's trapezoid with its red line, drawn at runtime so there is no icon file to ship.</summary>
internal static class TrayIconImage
{
    public static ImageSource Create()
    {
        const int size = 32;
        var body = new StreamGeometry();
        using (StreamGeometryContext context = body.Open())
        {
            context.BeginFigure(new Point(1, 4), isFilled: true, isClosed: true);
            context.LineTo(new Point(31, 4), isStroked: false, isSmoothJoin: false);
            context.LineTo(new Point(25, 24), isStroked: false, isSmoothJoin: false);
            context.LineTo(new Point(7, 24), isStroked: false, isSmoothJoin: false);
        }

        var visual = new DrawingVisual();
        using (DrawingContext drawing = visual.RenderOpen())
        {
            drawing.DrawGeometry(new SolidColorBrush(Color.FromRgb(0xE5, 0xE7, 0xEB)), null, body);
            drawing.DrawRectangle(new SolidColorBrush(Color.FromRgb(0xEF, 0x44, 0x44)), null, new Rect(9, 12, 14, 3));
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }
}
