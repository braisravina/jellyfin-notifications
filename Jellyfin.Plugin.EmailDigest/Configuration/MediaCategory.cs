namespace Jellyfin.Plugin.EmailDigest.Configuration;

/// <summary>
/// The digest section a monitored Jellyfin library is grouped under.
/// </summary>
/// <remarks>
/// Jellyfin has no built-in "Anime" library type: a library's <c>CollectionType</c> is only
/// "movies", "tvshows", "music", etc. Distinguishing Anime from Series is therefore an
/// admin-configured mapping from a specific library to a digest category, not something derived
/// automatically from Jellyfin metadata. The same holds for Documentaries and Other. A category is
/// never guessed from an item's type or a library's name; only the configured mapping counts. New
/// values are appended so persisted configuration keeps its meaning.
/// </remarks>
public enum MediaCategory
{
    /// <summary>
    /// Movies.
    /// </summary>
    Movies = 0,

    /// <summary>
    /// TV series.
    /// </summary>
    Series = 1,

    /// <summary>
    /// Anime, grouped separately from Series even though Jellyfin has no distinct library type for it.
    /// </summary>
    Anime = 2,

    /// <summary>
    /// Music.
    /// </summary>
    Music = 3,

    /// <summary>
    /// Documentaries, grouped separately from Movies and Series.
    /// </summary>
    Documentaries = 4,

    /// <summary>
    /// Anything the administrator wants digested that fits no other section.
    /// </summary>
    Other = 5
}
