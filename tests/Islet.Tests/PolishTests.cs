using Islet.App.Interop;
using Islet.App.Settings;
using Islet.App.ViewModels;
using Microsoft.Win32;

namespace Islet.Tests;

public class AppSettingsTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"islet-settings-{Guid.NewGuid():N}.json");

    public void Dispose() => File.Delete(_path);

    [Fact]
    public void Defaults_hide_in_fullscreen_on_the_primary_monitor()
    {
        AppSettings settings = AppSettings.Load(_path);
        Assert.True(settings.HideInFullscreen);
        Assert.Null(settings.MonitorDevice);
    }

    [Fact]
    public void Changes_are_saved_and_reloaded()
    {
        AppSettings settings = AppSettings.Load(_path);
        settings.SetHideInFullscreen(false);
        settings.SetMonitorDevice(@"\\.\DISPLAY2");

        AppSettings reloaded = AppSettings.Load(_path);
        Assert.False(reloaded.HideInFullscreen);
        Assert.Equal(@"\\.\DISPLAY2", reloaded.MonitorDevice);
    }

    [Fact]
    public void Changed_fires_only_for_real_changes()
    {
        AppSettings settings = AppSettings.Load(_path);
        int raised = 0;
        settings.Changed += () => raised++;

        settings.SetHideInFullscreen(true);
        settings.SetMonitorDevice(null);
        Assert.Equal(0, raised);

        settings.SetHideInFullscreen(false);
        settings.SetMonitorDevice("X");
        Assert.Equal(2, raised);
    }

    [Fact]
    public void A_blank_monitor_means_the_primary_one()
    {
        AppSettings settings = AppSettings.Load(_path);
        settings.SetMonitorDevice("X");
        settings.SetMonitorDevice("  ");
        Assert.Null(settings.MonitorDevice);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{\"hideInFullscreen\":\"yes\",\"monitorDevice\":7}")]
    [InlineData("")]
    public void A_damaged_file_falls_back_to_the_defaults(string content)
    {
        File.WriteAllText(_path, content);

        AppSettings settings = AppSettings.Load(_path);

        Assert.True(settings.HideInFullscreen);
        Assert.Null(settings.MonitorDevice);
    }
}

public class StartupRegistrationTests : IDisposable
{
    // A throwaway key, so the real Run entry is never touched by a test.
    private readonly string _keyPath = $@"Software\Islet\Tests\Run-{Guid.NewGuid():N}";

    public void Dispose() => Registry.CurrentUser.DeleteSubKeyTree(@"Software\Islet\Tests", throwOnMissingSubKey: false);

    [Fact]
    public void Nothing_is_written_until_it_is_turned_on()
    {
        StartupRegistration startup = New(@"C:\apps\Islet.App.exe");
        Assert.False(startup.IsEnabled);
        Assert.Null(Registry.CurrentUser.OpenSubKey(_keyPath));
    }

    [Fact]
    public void Turning_it_on_writes_the_quoted_exe_path_and_off_removes_it()
    {
        StartupRegistration startup = New(@"C:\Program Files\Islet\Islet.App.exe");

        Assert.True(startup.Set(true));
        Assert.True(startup.IsEnabled);
        using (RegistryKey key = Registry.CurrentUser.OpenSubKey(_keyPath)!)
        {
            Assert.Equal("\"C:\\Program Files\\Islet\\Islet.App.exe\"", key.GetValue("Islet"));
        }

        Assert.True(startup.Set(false));
        Assert.False(startup.IsEnabled);
    }

    [Fact]
    public void Turning_it_off_when_it_was_never_on_is_fine() => Assert.True(New(@"C:\a.exe").Set(false));

    [Fact]
    public void Turning_it_on_again_points_at_the_current_exe()
    {
        New(@"C:\old\Islet.App.exe").Set(true);
        New(@"C:\new\Islet.App.exe").Set(true);

        using RegistryKey key = Registry.CurrentUser.OpenSubKey(_keyPath)!;
        Assert.Equal("\"C:\\new\\Islet.App.exe\"", key.GetValue("Islet"));
    }

    [Fact]
    public void Without_an_exe_path_it_cannot_be_turned_on()
    {
        StartupRegistration startup = new(_keyPath, exePath: () => null);
        Assert.False(startup.Set(true));
        Assert.False(startup.IsEnabled);
    }

    [Fact]
    public void The_real_default_key_is_the_current_user_Run_key() =>
        Assert.Equal(@"Software\Microsoft\Windows\CurrentVersion\Run", StartupRegistration.RunKeyPath);

    private StartupRegistration New(string exe) => new(_keyPath, exePath: () => exe);
}

public class MonitorPlacementTests
{
    private static readonly MonitorInfo Primary = new(@"\\.\DISPLAY1", 0, 0, 1920, 1080, IsPrimary: true, Dpi: 120);
    private static readonly MonitorInfo Left = new(@"\\.\DISPLAY2", -2560, -200, 2560, 1440, IsPrimary: false, Dpi: 96);
    private static readonly MonitorInfo[] Both = [Primary, Left];

    [Fact]
    public void Centres_on_the_primary_touching_the_top_at_125_percent()
    {
        // 496 DIPs at 125% is 620 px, so (1920 - 620) / 2.
        Assert.Equal((650, 0), Monitors.TopCenter(Primary, 496));
    }

    [Fact]
    public void Another_monitor_uses_its_own_origin_size_and_scale()
    {
        // 496 px wide at 100%, on a monitor whose top-left is (-2560, -200).
        Assert.Equal((-2560 + ((2560 - 496) / 2), -200), Monitors.TopCenter(Left, 496));
    }

    [Fact]
    public void A_named_monitor_is_chosen()
    {
        Assert.Equal(Left, Monitors.Choose(Both, @"\\.\DISPLAY2"));
        Assert.Equal(Left, Monitors.Choose(Both, @"\\.\display2"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(@"\\.\DISPLAY9")]
    public void No_choice_or_an_unplugged_one_falls_back_to_the_primary(string? device) =>
        Assert.Equal(Primary, Monitors.Choose(Both, device));

    [Fact]
    public void With_no_primary_flag_the_first_monitor_is_used() =>
        Assert.Equal(Left, Monitors.Choose([Left], null));

    [Fact]
    public void No_monitors_means_no_choice() => Assert.Null(Monitors.Choose([], null));

    [Fact]
    public void The_real_machine_reports_at_least_one_monitor_with_a_primary()
    {
        IReadOnlyList<MonitorInfo> monitors = Monitors.List();

        Assert.NotEmpty(monitors);
        Assert.True(monitors[0].IsPrimary);
        Assert.All(monitors, monitor =>
        {
            Assert.StartsWith(@"\\.\DISPLAY", monitor.DeviceName);
            Assert.True(monitor.Width > 0 && monitor.Height > 0);
            Assert.True(monitor.Dpi >= 96);
        });
    }

    [Fact]
    public void Labels_name_the_resolution_and_mark_the_primary()
    {
        Assert.Equal("Display 1 (1920×1080) · primary", Primary.Label(1));
        Assert.Equal("Display 2 (2560×1440)", Left.Label(2));
    }
}

public class FullscreenVisibilityTests
{
    [Theory]
    [InlineData(OverlayView.Idle)]
    [InlineData(OverlayView.MediaCompact)]
    [InlineData(OverlayView.MediaExpanded)]
    [InlineData(OverlayView.AgentCompact)]
    [InlineData(OverlayView.ClockCompact)]
    [InlineData(OverlayView.Notification)]
    public void Ordinary_views_hide_in_a_fullscreen_app(OverlayView view) =>
        Assert.True(OverlayVisibility.ShouldHide(true, true, view));

    [Theory]
    [InlineData(OverlayView.Permission)]
    [InlineData(OverlayView.AgentDone)]
    public void Things_that_need_you_break_through(OverlayView view) =>
        Assert.False(OverlayVisibility.ShouldHide(true, true, view));

    [Theory]
    [InlineData(OverlayView.Idle)]
    [InlineData(OverlayView.Permission)]
    public void Nothing_hides_when_the_setting_is_off(OverlayView view) =>
        Assert.False(OverlayVisibility.ShouldHide(false, true, view));

    [Theory]
    [InlineData(OverlayView.Idle)]
    [InlineData(OverlayView.MediaCompact)]
    public void Nothing_hides_without_a_fullscreen_app(OverlayView view) =>
        Assert.False(OverlayVisibility.ShouldHide(true, false, view));
}
