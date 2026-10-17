using System.IO;
using System.Text.Json;

namespace Islet.App.Settings;

/// <summary>
/// The few user settings, kept as JSON in %LOCALAPPDATA%\Islet\settings.json. "Start with
/// Windows" isn't stored here: the Run registry value is the single source of truth for it.
/// </summary>
public sealed class AppSettings
{
    private readonly string _path;

    public AppSettings(string? path = null) =>
        _path = path ?? Path.Combine(Hook.ProductInfo.LogDirectory, "..", "settings.json");

    /// <summary>Raised after any setting changes (and is saved).</summary>
    public event Action? Changed;

    /// <summary>Hide the island while a fullscreen app is in front, except for things that need you.</summary>
    public bool HideInFullscreen { get; private set; } = true;

    /// <summary>The device name of the monitor to sit on (e.g. <c>\\.\DISPLAY2</c>), or null for the primary one.</summary>
    public string? MonitorDevice { get; private set; }

    public static AppSettings Load(string? path = null)
    {
        var settings = new AppSettings(path);
        try
        {
            if (File.Exists(settings._path))
            {
                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(settings._path));
                JsonElement root = document.RootElement;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    if (root.TryGetProperty("hideInFullscreen", out JsonElement hide) &&
                        hide.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    {
                        settings.HideInFullscreen = hide.GetBoolean();
                    }

                    if (root.TryGetProperty("monitorDevice", out JsonElement monitor) && monitor.ValueKind == JsonValueKind.String)
                    {
                        string? device = monitor.GetString();
                        settings.MonitorDevice = string.IsNullOrWhiteSpace(device) ? null : device;
                    }
                }
            }
        }
        catch (Exception exception)
        {
            // A damaged settings file falls back to the defaults rather than stopping the app.
            FileLog.Write("Settings load failed, using defaults", exception);
        }

        return settings;
    }

    public void SetHideInFullscreen(bool value)
    {
        if (HideInFullscreen == value)
        {
            return;
        }

        HideInFullscreen = value;
        SaveAndNotify();
    }

    public void SetMonitorDevice(string? device)
    {
        string? normalized = string.IsNullOrWhiteSpace(device) ? null : device;
        if (MonitorDevice == normalized)
        {
            return;
        }

        MonitorDevice = normalized;
        SaveAndNotify();
    }

    private void SaveAndNotify()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
            File.WriteAllText(
                _path,
                JsonSerializer.Serialize(new { hideInFullscreen = HideInFullscreen, monitorDevice = MonitorDevice }));
        }
        catch (Exception exception)
        {
            FileLog.Write("Settings save failed", exception);
        }

        Changed?.Invoke();
    }
}
