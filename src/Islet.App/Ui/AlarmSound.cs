using System.Media;
using System.Windows.Threading;

namespace Islet.App.Ui;

/// <summary>Repeats a system sound while an alarm rings, and gives up after a minute.</summary>
public sealed class AlarmSound
{
    private const int MaxRepeats = 40;

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(1500) };
    private int _plays;

    public AlarmSound() => _timer.Tick += (_, _) => PlayOnce();

    public void Start()
    {
        if (_timer.IsEnabled)
        {
            return;
        }

        _plays = 0;
        PlayOnce();
        _timer.Start();
    }

    public void Stop() => _timer.Stop();

    private void PlayOnce()
    {
        if (++_plays > MaxRepeats)
        {
            _timer.Stop();
            return;
        }

        SystemSounds.Exclamation.Play();
    }
}
