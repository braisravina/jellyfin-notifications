using System;
using Jellyfin.Plugin.EmailDigest.Configuration;

namespace Jellyfin.Plugin.EmailDigest.Models;

/// <summary>
/// A single normalized, already-authorized digest candidate.
/// </summary>
/// <remarks>
/// Produced only after Jellyfin's own authorization has already run. Carries just the fields
/// later digest phases need, never the
/// underlying Jellyfin <c>BaseItem</c> graph. Series/season metadata only applies to
/// <see cref="DiscoveredMediaItemType.Episode"/>; album-artist metadata only applies to
/// <see cref="DiscoveredMediaItemType.MusicAlbum"/>. Missing metadata is left <see
/// langword="null"/>, never fabricated.
/// </remarks>
public class DiscoveredMediaItem
{
    /// <summary>
    /// Gets or sets the stable Jellyfin item id.
    /// </summary>
    public Guid ItemId { get; set; }

    /// <summary>
    /// Gets or sets the stable id of the monitored library this item was found in. Library ids
    /// are used rather than names because administrators can rename libraries.
    /// </summary>
    public Guid LibraryId { get; set; }

    /// <summary>
    /// Gets or sets the digest category, taken from the administrator's configured mapping for
    /// <see cref="LibraryId"/>. Never inferred from the item type or the library's name.
    /// </summary>
    public MediaCategory Category { get; set; }

    /// <summary>
    /// Gets or sets the normalized media kind.
    /// </summary>
    public DiscoveredMediaItemType ItemType { get; set; }

    /// <summary>
    /// Gets or sets the item's own display name (movie title, episode title, or album title).
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the production year, when Jellyfin has one.
    /// </summary>
    public int? ProductionYear { get; set; }

    /// <summary>
    /// Gets or sets the UTC instant Jellyfin recorded this item as added.
    /// </summary>
    public DateTimeOffset DateAddedUtc { get; set; }

    /// <summary>
    /// Gets or sets the parent series id. Only set for <see cref="DiscoveredMediaItemType.Episode"/>.
    /// </summary>
    public Guid? SeriesId { get; set; }

    /// <summary>
    /// Gets or sets the parent series name. Only set for <see cref="DiscoveredMediaItemType.Episode"/>.
    /// </summary>
    public string? SeriesName { get; set; }

    /// <summary>
    /// Gets or sets the season number. Only set for <see cref="DiscoveredMediaItemType.Episode"/>,
    /// and only when Jellyfin has one.
    /// </summary>
    public int? SeasonNumber { get; set; }

    /// <summary>
    /// Gets or sets the episode number. Only set for <see cref="DiscoveredMediaItemType.Episode"/>,
    /// and only when Jellyfin has one.
    /// </summary>
    public int? EpisodeNumber { get; set; }

    /// <summary>
    /// Gets or sets the album artist display name. Only set for
    /// <see cref="DiscoveredMediaItemType.MusicAlbum"/>, and only when Jellyfin has one.
    /// </summary>
    public string? AlbumArtist { get; set; }
}
