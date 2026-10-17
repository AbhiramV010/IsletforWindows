using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Islet.App.ViewModels;

/// <summary>The three pill sizes. Compact shows media or a one-line agent status.</summary>
public enum OverlayState
{
    Collapsed,
    Compact,
    Expanded,
}

/// <summary>
/// State of the pill itself. Media and agent state get their own view models in
/// the later milestones and will feed this one.
/// </summary>
public sealed class OverlayViewModel : INotifyPropertyChanged
{
    private OverlayState _state = OverlayState.Collapsed;

    public event PropertyChangedEventHandler? PropertyChanged;

    public OverlayState State
    {
        get => _state;
        set
        {
            if (_state == value)
            {
                return;
            }

            _state = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Debug path used by the hotkey and by <c>--demo</c>.</summary>
    public void CycleState() => State = State switch
    {
        OverlayState.Collapsed => OverlayState.Compact,
        OverlayState.Compact => OverlayState.Expanded,
        _ => OverlayState.Collapsed,
    };

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
