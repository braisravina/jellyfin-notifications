namespace Jellyfin.Plugin.EmailDigest.Digest;

/// <summary>
/// What one line of a digest section represents.
/// </summary>
public enum DigestEntryKind
{
    /// <summary>
    /// A single movie.
    /// </summary>
    Movie = 0,

    /// <summary>
    /// A series (or anime) with one or more new episodes.
    /// </summary>
    Series = 1,

    /// <summary>
    /// A music album.
    /// </summary>
    Album = 2
}
