namespace Jellyfin.Plugin.EmailDigest.Models;

/// <summary>
/// The supported digest-candidate media kinds. Nothing else can appear in a
/// <see cref="DiscoveredMediaItem"/>.
/// </summary>
public enum DiscoveredMediaItemType
{
    /// <summary>
    /// A movie.
    /// </summary>
    Movie = 0,

    /// <summary>
    /// A single TV (or anime) episode.
    /// </summary>
    Episode = 1,

    /// <summary>
    /// A music album, standing in for all of its tracks.
    /// </summary>
    MusicAlbum = 2
}
