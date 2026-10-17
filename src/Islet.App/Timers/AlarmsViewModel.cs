using System.IO;
using System.Text.Json;
using Islet.App.ViewModels;

namespace Islet.App.Timers;

/// <summary>One alarm, due at an absolute time.</summary>
public sealed record AlarmItem(Guid Id, DateTime DueUtc);

/// <summary>Where pending alarms survive an app restart.</summary>
public interface IAlarmStore
{
    IReadOnlyList<DateTime> Load();

    void Save(IReadOnlyList<DateTime> dueUtc);
}

/// <summary>Pending alarms as a JSON list of UTC times in %LOCALAPPDATA%\Islet\alarms.json. Failures are logged and ignored.</summary>
public sealed class FileAlarmStore : IAlarmStore
{
    private readonly string _path;

    public FileAlarmStore(string? path = null) =>
        _path = path ?? Path.Combine(Hook.ProductInfo.LogDirectory, "..", "alarms.json");

    public IReadOnlyList<DateTime> Load()
    {
        try
        {
            return File.Exists(_path)
                ? JsonSerializer.Deserialize<List<DateTime>>(File.ReadAllText(_path)) ?? []
                : [];
        }
        catch (Exception exception)
        {
            FileLog.Write("Alarm store load failed", exception);
            return [];
        }
    }

    public void Save(IReadOnlyList<DateTime> dueUtc)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
            File.WriteAllText(_path, JsonSerializer.Serialize(dueUtc));
        }
        catch (Exception exception)
        {
            FileLog.Write("Alarm store save failed", exception);
        }
    }
}

/// <summary>
/// One-shot alarms. <see cref="Tick"/> moves an alarm that has come due into
/// <see cref="Ringing"/>, where it stays until it is dismissed or snoozed. An alarm that comes
/// due while another rings waits its turn.
/// </summary>
public sealed class AlarmsViewModel : ObservableObject
{
    public static readonly TimeSpan SnoozeFor = TimeSpan.FromMinutes(5);

    /// <summary>An alarm that was missed by longer than this while the app was closed is dropped on load.</summary>
    public static readonly TimeSpan MaxMissedOnLoad = TimeSpan.FromHours(1);

    private readonly Func<DateTime> _utcNow;
    private readonly IAlarmStore? _store;
    private readonly List<AlarmItem> _pending = [];
    private AlarmItem? _ringing;

    public AlarmsViewModel(Func<DateTime>? utcNow = null, IAlarmStore? store = null)
    {
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _store = store;

        DateTime now = _utcNow();
        foreach (DateTime due in _store?.Load() ?? [])
        {
            if (now - due <= MaxMissedOnLoad)
            {
                _pending.Add(new AlarmItem(Guid.NewGuid(), due));
            }
        }

        _pending.Sort((a, b) => a.DueUtc.CompareTo(b.DueUtc));
    }

    /// <summary>The alarm that is going off now, or null.</summary>
    public AlarmItem? Ringing
    {
        get => _ringing;
        private set => SetField(ref _ringing, value);
    }

    public int PendingCount => _pending.Count;

    public bool HasPending => _pending.Count > 0;

    /// <summary>The soonest alarm that hasn't gone off yet.</summary>
    public AlarmItem? Next => _pending.Count > 0 ? _pending[0] : null;

    public TimeSpan? NextRemaining => Next is { } next ? next.DueUtc - _utcNow() : null;

    /// <summary>"4:12", or "1:02:03" past an hour. Empty with no alarm.</summary>
    public string NextText => NextRemaining is { } remaining ? FormatRemaining(remaining) : "";

    /// <summary>The local clock time the ringing alarm was set for, e.g. "07:45".</summary>
    public string RingingAt => Ringing is { } ringing ? ringing.DueUtc.ToLocalTime().ToString("HH:mm") : "";

    public AlarmItem AddIn(TimeSpan span)
    {
        var alarm = new AlarmItem(Guid.NewGuid(), _utcNow() + span);
        _pending.Add(alarm);
        _pending.Sort((a, b) => a.DueUtc.CompareTo(b.DueUtc));
        Changed();
        return alarm;
    }

    /// <summary>Starts ringing anything that is due. Called by the timer, and directly by tests.</summary>
    public void Tick()
    {
        if (Ringing is null && _pending.Count > 0 && _pending[0].DueUtc <= _utcNow())
        {
            Ringing = _pending[0];
            _pending.RemoveAt(0);
            Changed();
            OnPropertyChanged(nameof(RingingAt));
        }

        // The countdown text moves every second.
        OnPropertyChanged(nameof(NextText));
    }

    public void Snooze()
    {
        if (Ringing is null)
        {
            return;
        }

        Ringing = null;
        AddIn(SnoozeFor);
        Tick();
    }

    public void Dismiss()
    {
        if (Ringing is null)
        {
            return;
        }

        Ringing = null;
        Tick();
        Changed();
    }

    /// <summary>Stops the ringing alarm. With none ringing, cancels every pending alarm instead.</summary>
    public void DismissOrClear()
    {
        if (Ringing is not null)
        {
            Dismiss();
            return;
        }

        if (_pending.Count > 0)
        {
            _pending.Clear();
            Changed();
        }
    }

    public static string FormatRemaining(TimeSpan remaining)
    {
        if (remaining < TimeSpan.Zero)
        {
            remaining = TimeSpan.Zero;
        }

        // Round up, so the last second reads 0:01 rather than 0:00.
        remaining = TimeSpan.FromSeconds(Math.Ceiling(remaining.TotalSeconds));
        return remaining.TotalHours >= 1
            ? $"{(int)remaining.TotalHours}:{remaining.Minutes:00}:{remaining.Seconds:00}"
            : $"{remaining.Minutes}:{remaining.Seconds:00}";
    }

    private void Changed()
    {
        OnPropertyChanged(nameof(PendingCount));
        OnPropertyChanged(nameof(HasPending));
        OnPropertyChanged(nameof(Next));
        OnPropertyChanged(nameof(NextRemaining));
        OnPropertyChanged(nameof(NextText));
        _store?.Save(_pending.Select(alarm => alarm.DueUtc).ToList());
    }
}
