namespace Islet.App.Media;

public enum SourceKind
{
    /// <summary>Video, games, anything that isn't music. Never shown.</summary>
    Other,

    /// <summary>A music app, matched by its app id.</summary>
    Music,

    /// <summary>A browser or browser app window. Could be YouTube Music, could be Netflix.</summary>
    Browser,
}

/// <summary>
/// Decides which media sessions the island may show. Music apps are matched by app id. Web
/// players (YouTube Music in a tab or installed as a browser app) all report the browser's id,
/// so they are told apart by metadata: music players fill in the album, while video sites such
/// as Netflix and YouTube do not.
/// </summary>
public static class MusicSources
{
    private static readonly string[] MusicApps =
    [
        "Spotify",
        "AppleMusic",
        "Apple Music",
        "iTunes",
        "YouTubeMusic",
        "YouTube Music",
        "youtube-music",
        "Tidal",
        "Deezer",
        "AmazonMusic",
        "Amazon Music",
        "ZuneMusic",
        "Groove",
        "foobar2000",
        "MusicBee",
        "AIMP",
        "Winamp",
        "Plexamp",
        "Qobuz",
        "Pandora",
        "SoundCloud",
        "Cider",
    ];

    private static readonly string[] Browsers =
    [
        "chrome",
        "msedge",
        "firefox",
        "brave",
        "opera",
        "vivaldi",
        "browsercompany",
        "_crx_",
    ];

    public static SourceKind Classify(string? appUserModelId)
    {
        if (string.IsNullOrEmpty(appUserModelId))
        {
            return SourceKind.Other;
        }

        if (ContainsAny(appUserModelId, MusicApps))
        {
            return SourceKind.Music;
        }

        return ContainsAny(appUserModelId, Browsers) ? SourceKind.Browser : SourceKind.Other;
    }

    public static bool IsSpotify(string? appUserModelId) =>
        appUserModelId?.Contains("Spotify", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>True when a session of this kind, with this album title, counts as music.</summary>
    public static bool IsMusic(SourceKind kind, string? albumTitle) => kind switch
    {
        SourceKind.Music => true,
        SourceKind.Browser => !string.IsNullOrWhiteSpace(albumTitle),
        _ => false,
    };

    private static bool ContainsAny(string value, string[] needles)
    {
        foreach (string needle in needles)
        {
            if (value.Contains(needle, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
