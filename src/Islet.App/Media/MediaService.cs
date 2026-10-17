using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace Islet.App.Media;

/// <summary>Title, artist and art of the tracked session. Art is frozen, so any thread may use it.</summary>
public sealed record MediaTrack(string Title, string Artist, ImageSource? Art);

/// <summary>Play state and which transport buttons the session currently allows.</summary>
public sealed record MediaPlayback(bool IsPlaying, bool CanPlayPause, bool CanNext, bool CanPrevious);

/// <summary>
/// Follows one system media session through GlobalSystemMediaTransportControls, preferring
/// Spotify and falling back to the current session. Purely event-driven, no polling.
/// Events fire on WinRT threads; subscribers marshal to the UI thread themselves.
/// </summary>
public sealed class MediaService : IDisposable
{
    private const int ArtDecodeSize = 128;

    private readonly object _gate = new();
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private int _trackGeneration;
    private bool _disposed;

    /// <summary>The tracked session went away (null) or switched to another app.</summary>
    public event Action<string?>? SessionChanged;

    public event Action<MediaTrack>? TrackChanged;

    public event Action<MediaPlayback>? PlaybackChanged;

    public async Task StartAsync()
    {
        GlobalSystemMediaTransportControlsSessionManager manager =
            await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _manager = manager;
            _manager.SessionsChanged += OnSessionsChanged;
            _manager.CurrentSessionChanged += OnCurrentSessionChanged;
        }

        SelectSession();
    }

    public Task TogglePlayPauseAsync() => Send(session => session.TryTogglePlayPauseAsync().AsTask());

    public Task NextAsync() => Send(session => session.TrySkipNextAsync().AsTask());

    public Task PreviousAsync() => Send(session => session.TrySkipPreviousAsync().AsTask());

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            if (_manager is not null)
            {
                _manager.SessionsChanged -= OnSessionsChanged;
                _manager.CurrentSessionChanged -= OnCurrentSessionChanged;
                _manager = null;
            }

            Track(null);
        }
    }

    private void OnSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args) =>
        SelectSession();

    private void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args) =>
        SelectSession();

    private void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args) =>
        _ = RefreshTrackAsync(sender);

    private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args) =>
        RefreshPlayback(sender);

    private void SelectSession()
    {
        GlobalSystemMediaTransportControlsSession? session;
        lock (_gate)
        {
            if (_manager is null)
            {
                return;
            }

            session = PickSession(_manager);
            // By reference: a restarted Spotify has the same app id but a new session.
            if (ReferenceEquals(session, _session))
            {
                return;
            }

            Track(session);
        }

        FileLog.Write($"Media session: {session?.SourceAppUserModelId ?? "none"}");
        SessionChanged?.Invoke(session?.SourceAppUserModelId);

        if (session is not null)
        {
            RefreshPlayback(session);
            _ = RefreshTrackAsync(session);
        }
    }

    private static GlobalSystemMediaTransportControlsSession? PickSession(GlobalSystemMediaTransportControlsSessionManager manager)
    {
        foreach (GlobalSystemMediaTransportControlsSession candidate in manager.GetSessions())
        {
            if (candidate.SourceAppUserModelId.Contains("Spotify", StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return manager.GetCurrentSession();
    }

    /// <summary>Moves the per-session subscriptions to <paramref name="session"/>. Caller holds the gate.</summary>
    private void Track(GlobalSystemMediaTransportControlsSession? session)
    {
        if (_session is not null)
        {
            _session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            _session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
        }

        _session = session;
        Interlocked.Increment(ref _trackGeneration);

        if (_session is not null)
        {
            _session.MediaPropertiesChanged += OnMediaPropertiesChanged;
            _session.PlaybackInfoChanged += OnPlaybackInfoChanged;
        }
    }

    private void RefreshPlayback(GlobalSystemMediaTransportControlsSession session)
    {
        try
        {
            GlobalSystemMediaTransportControlsSessionPlaybackInfo info = session.GetPlaybackInfo();
            GlobalSystemMediaTransportControlsSessionPlaybackControls controls = info.Controls;
            PlaybackChanged?.Invoke(new MediaPlayback(
                info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,
                controls.IsPlayPauseToggleEnabled || controls.IsPlayEnabled || controls.IsPauseEnabled,
                controls.IsNextEnabled,
                controls.IsPreviousEnabled));
        }
        catch (Exception exception)
        {
            FileLog.Write("Media playback refresh failed", exception);
        }
    }

    private async Task RefreshTrackAsync(GlobalSystemMediaTransportControlsSession session)
    {
        // Property events come in bursts; only the newest refresh gets to publish.
        int generation = Interlocked.Increment(ref _trackGeneration);
        try
        {
            GlobalSystemMediaTransportControlsSessionMediaProperties properties = await session.TryGetMediaPropertiesAsync();
            ImageSource? art = await LoadArtAsync(properties.Thumbnail);

            if (generation != Volatile.Read(ref _trackGeneration))
            {
                return;
            }

            TrackChanged?.Invoke(new MediaTrack(properties.Title ?? "", properties.Artist ?? "", art));
        }
        catch (Exception exception)
        {
            FileLog.Write("Media properties refresh failed", exception);
        }
    }

    private static async Task<ImageSource?> LoadArtAsync(IRandomAccessStreamReference? thumbnail)
    {
        if (thumbnail is null)
        {
            return null;
        }

        try
        {
            var buffer = new MemoryStream();
            using (IRandomAccessStreamWithContentType winrtStream = await thumbnail.OpenReadAsync())
            using (Stream stream = winrtStream.AsStreamForRead())
            {
                await stream.CopyToAsync(buffer);
            }

            buffer.Position = 0;
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = ArtDecodeSize;
            image.StreamSource = buffer;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception exception)
        {
            FileLog.Write("Album art load failed", exception);
            return null;
        }
    }

    private async Task Send(Func<GlobalSystemMediaTransportControlsSession, Task<bool>> command)
    {
        GlobalSystemMediaTransportControlsSession? session;
        lock (_gate)
        {
            session = _session;
        }

        if (session is null)
        {
            return;
        }

        try
        {
            await command(session);
        }
        catch (Exception exception)
        {
            FileLog.Write("Media command failed", exception);
        }
    }
}
