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
/// Follows one music session through GlobalSystemMediaTransportControls. Only music is
/// eligible (see <see cref="MusicSources"/>): Spotify and other music apps, plus browser
/// players that report an album. Anything else, such as Netflix, is ignored. Purely
/// event-driven, no polling. Events fire on WinRT threads; subscribers marshal to the UI
/// thread themselves.
/// </summary>
public sealed class MediaService : IDisposable
{
    private const int ArtDecodeSize = 128;

    private readonly object _gate = new();
    private readonly SemaphoreSlim _selectLock = new(1, 1);
    private readonly List<GlobalSystemMediaTransportControlsSession> _watched = [];
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

        await SelectSessionAsync();
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

            foreach (GlobalSystemMediaTransportControlsSession watched in _watched)
            {
                watched.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            }

            _watched.Clear();
            Track(null);
        }
    }

    private void OnSessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args) =>
        _ = SelectSessionAsync();

    private void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args) =>
        _ = SelectSessionAsync();

    private void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args) =>
        _ = OnPropertiesChangedAsync(sender);

    private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args) =>
        RefreshPlayback(sender);

    /// <summary>
    /// A browser player's metadata arrives after its session appears, and a tab can switch from
    /// a video to a song, so every watched session's property change re-runs the selection.
    /// </summary>
    private async Task OnPropertiesChangedAsync(GlobalSystemMediaTransportControlsSession sender)
    {
        await SelectSessionAsync();

        bool stillTracked;
        lock (_gate)
        {
            stillTracked = ReferenceEquals(_session, sender);
        }

        if (stillTracked)
        {
            await RefreshTrackAsync(sender);
        }
    }

    private async Task SelectSessionAsync()
    {
        await _selectLock.WaitAsync();
        try
        {
            GlobalSystemMediaTransportControlsSessionManager? manager;
            lock (_gate)
            {
                manager = _manager;
            }

            if (manager is null)
            {
                return;
            }

            IReadOnlyList<GlobalSystemMediaTransportControlsSession> sessions = manager.GetSessions();
            GlobalSystemMediaTransportControlsSession? chosen = await PickMusicSessionAsync(sessions);

            lock (_gate)
            {
                if (_disposed)
                {
                    return;
                }

                SyncWatchers(sessions);

                // By reference: a restarted Spotify has the same app id but a new session.
                if (ReferenceEquals(chosen, _session))
                {
                    return;
                }

                Track(chosen);
            }

            FileLog.Write($"Media session: {chosen?.SourceAppUserModelId ?? "none"}");
            SessionChanged?.Invoke(chosen?.SourceAppUserModelId);

            if (chosen is not null)
            {
                RefreshPlayback(chosen);
                _ = RefreshTrackAsync(chosen);
            }
        }
        catch (Exception exception)
        {
            FileLog.Write("Media session selection failed", exception);
        }
        finally
        {
            _selectLock.Release();
        }
    }

    /// <summary>
    /// The best music session: one that is playing beats one that is paused, and within each
    /// group Spotify beats other music apps, which beat browser players.
    /// </summary>
    private static async Task<GlobalSystemMediaTransportControlsSession?> PickMusicSessionAsync(
        IReadOnlyList<GlobalSystemMediaTransportControlsSession> sessions)
    {
        GlobalSystemMediaTransportControlsSession? best = null;
        (bool Paused, int Tier) bestRank = (true, int.MaxValue);

        foreach (GlobalSystemMediaTransportControlsSession candidate in sessions)
        {
            string appId = candidate.SourceAppUserModelId;
            SourceKind kind = MusicSources.Classify(appId);
            if (kind == SourceKind.Other)
            {
                continue;
            }

            if (kind == SourceKind.Browser)
            {
                string? album = null;
                try
                {
                    album = (await candidate.TryGetMediaPropertiesAsync()).AlbumTitle;
                }
                catch (Exception exception)
                {
                    FileLog.Write($"Media properties unavailable for {appId}", exception);
                }

                if (!MusicSources.IsMusic(kind, album))
                {
                    continue;
                }
            }

            bool paused = candidate.GetPlaybackInfo().PlaybackStatus !=
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            int tier = MusicSources.IsSpotify(appId) ? 0 : kind == SourceKind.Music ? 1 : 2;

            // False sorts before true, so a playing session outranks a paused one.
            if (best is null || (paused, tier).CompareTo(bestRank) < 0)
            {
                best = candidate;
                bestRank = (paused, tier);
            }
        }

        return best;
    }

    /// <summary>Subscribes to property changes on every session, so a late-arriving album can qualify one. Caller holds the gate.</summary>
    private void SyncWatchers(IReadOnlyList<GlobalSystemMediaTransportControlsSession> sessions)
    {
        for (int i = _watched.Count - 1; i >= 0; i--)
        {
            if (!sessions.Contains(_watched[i]))
            {
                _watched[i].MediaPropertiesChanged -= OnMediaPropertiesChanged;
                _watched.RemoveAt(i);
            }
        }

        foreach (GlobalSystemMediaTransportControlsSession session in sessions)
        {
            if (!_watched.Contains(session) && MusicSources.Classify(session.SourceAppUserModelId) != SourceKind.Other)
            {
                session.MediaPropertiesChanged += OnMediaPropertiesChanged;
                _watched.Add(session);
            }
        }
    }

    /// <summary>Moves the playback subscription to <paramref name="session"/>. Caller holds the gate.</summary>
    private void Track(GlobalSystemMediaTransportControlsSession? session)
    {
        if (_session is not null)
        {
            _session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
        }

        _session = session;
        Interlocked.Increment(ref _trackGeneration);

        if (_session is not null)
        {
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
