using System.Windows.Media;
using Islet.App.Media;

namespace Islet.App.ViewModels;

/// <summary>What the island shows for media. Updated on the UI thread from <see cref="MediaService"/>.</summary>
public sealed class MediaViewModel : ObservableObject
{
    private readonly MediaService? _service;
    private bool _hasSession;
    private bool _isPlaying;
    private string _title = "";
    private string _artist = "";
    private ImageSource? _art;
    private bool _canPlayPause;
    private bool _canNext;
    private bool _canPrevious;

    public MediaViewModel(MediaService? service) => _service = service;

    public bool HasSession
    {
        get => _hasSession;
        private set => SetField(ref _hasSession, value);
    }

    public bool IsPlaying
    {
        get => _isPlaying;
        private set => SetField(ref _isPlaying, value);
    }

    public string Title
    {
        get => _title;
        private set => SetField(ref _title, value);
    }

    public string Artist
    {
        get => _artist;
        private set => SetField(ref _artist, value);
    }

    public ImageSource? Art
    {
        get => _art;
        private set => SetField(ref _art, value);
    }

    public bool CanPlayPause
    {
        get => _canPlayPause;
        private set => SetField(ref _canPlayPause, value);
    }

    public bool CanNext
    {
        get => _canNext;
        private set => SetField(ref _canNext, value);
    }

    public bool CanPrevious
    {
        get => _canPrevious;
        private set => SetField(ref _canPrevious, value);
    }

    public void ApplySession(string? sourceAppId)
    {
        HasSession = sourceAppId is not null;
        if (!HasSession)
        {
            Apply(new MediaTrack("", "", null));
            Apply(new MediaPlayback(false, false, false, false));
        }
    }

    public void Apply(MediaTrack track)
    {
        Title = track.Title;
        Artist = track.Artist;
        Art = track.Art;
    }

    public void Apply(MediaPlayback playback)
    {
        IsPlaying = playback.IsPlaying;
        CanPlayPause = playback.CanPlayPause;
        CanNext = playback.CanNext;
        CanPrevious = playback.CanPrevious;
    }

    public void TogglePlayPause() => _ = _service?.TogglePlayPauseAsync();

    public void Next() => _ = _service?.NextAsync();

    public void Previous() => _ = _service?.PreviousAsync();
}
