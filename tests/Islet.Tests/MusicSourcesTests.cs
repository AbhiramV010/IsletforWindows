using Islet.App.Media;

namespace Islet.Tests;

public class MusicSourcesTests
{
    [Theory]
    [InlineData("Spotify.exe")]
    [InlineData("SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify")]
    [InlineData("AppleInc.AppleMusicWin_nzyj5cx40ttqa!App")]
    [InlineData("AppleInc.iTunes_nzyj5cx40ttqa!iTunes")]
    [InlineData("com.github.th-ch.youtube-music")]
    [InlineData("TIDAL.exe")]
    [InlineData("Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic")]
    public void Music_apps_are_recognised(string appId) =>
        Assert.Equal(SourceKind.Music, MusicSources.Classify(appId));

    [Theory]
    [InlineData("Chrome")]
    [InlineData("MSEdge")]
    [InlineData("Brave.Brave")]
    [InlineData("Brave._crx_eppojlglocmnohnlnionkobfln")]
    public void Browser_sessions_are_classed_as_browsers(string appId) =>
        Assert.Equal(SourceKind.Browser, MusicSources.Classify(appId));

    [Fact]
    public void Firefox_has_a_hashed_id_so_it_is_ignored() =>
        Assert.Equal(SourceKind.Other, MusicSources.Classify("308046B0AF4A39CB"));

    [Theory]
    [InlineData("Microsoft.Media.Player")]
    [InlineData("Valve.Steam")]
    [InlineData("")]
    [InlineData(null)]
    public void Everything_else_is_ignored(string? appId) =>
        Assert.Equal(SourceKind.Other, MusicSources.Classify(appId));

    [Fact]
    public void Music_apps_always_count_as_music() =>
        Assert.True(MusicSources.IsMusic(SourceKind.Music, albumTitle: null));

    [Theory]
    [InlineData("Some Album", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(null, false)]
    public void Browser_players_count_as_music_only_with_an_album(string? album, bool expected) =>
        Assert.Equal(expected, MusicSources.IsMusic(SourceKind.Browser, album));

    [Fact]
    public void Other_sources_never_count_as_music() =>
        Assert.False(MusicSources.IsMusic(SourceKind.Other, "Some Album"));

    [Fact]
    public void Spotify_is_recognised_case_insensitively()
    {
        Assert.True(MusicSources.IsSpotify("spotify.exe"));
        Assert.False(MusicSources.IsSpotify("Chrome"));
    }
}
