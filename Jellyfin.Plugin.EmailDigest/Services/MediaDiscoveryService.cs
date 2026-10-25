using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.EmailDigest.Configuration;
using Jellyfin.Plugin.EmailDigest.Models;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.EmailDigest.Services;

/// <inheritdoc />
/// <remarks>
/// Authorization comes entirely from Jellyfin, in two layers. Library access is decided by
/// <see cref="ILibraryAccessResolver"/> (Jellyfin's own <c>CollectionFolder.IsVisible</c>), because
/// <see cref="MediaBrowser.Controller.Library.ILibraryManager.GetItemList(InternalItemsQuery)"/> only
/// intersects a query with the user's permitted libraries when no <c>TopParentIds</c> are supplied —
/// and this service always supplies them. Item-level rules (parental rating, blocked tags) come from
/// building the <see cref="InternalItemsQuery"/> with its <c>User</c> set. Only libraries that are
/// both monitored and accessible are ever queried; if none are, or the user is unresolved, or the
/// window is empty/inverted, the result is empty without running a query, so an empty
/// <c>TopParentIds</c> (which Jellyfin reads as "all libraries") can never be sent. A Jellyfin
/// failure surfaces as <see cref="MediaDiscoveryException"/> rather than an empty result.
/// </remarks>
public sealed class MediaDiscoveryService : IMediaDiscoveryService
{
    private static readonly BaseItemKind[] SupportedKinds =
    {
        BaseItemKind.Movie,
        BaseItemKind.Episode,
        BaseItemKind.MusicAlbum
    };

    private readonly ILibraryManager _libraryManager;
    private readonly IUserManager _userManager;
    private readonly ILibraryAccessResolver _libraryAccess;
    private readonly ILogger<MediaDiscoveryService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="MediaDiscoveryService"/> class.
    /// </summary>
    /// <param name="libraryManager">Jellyfin's library manager.</param>
    /// <param name="userManager">Jellyfin's user manager.</param>
    /// <param name="libraryAccess">Resolves whether a monitored library is accessible to a user.</param>
    /// <param name="logger">Logger.</param>
    public MediaDiscoveryService(
        ILibraryManager libraryManager,
        IUserManager userManager,
        ILibraryAccessResolver libraryAccess,
        ILogger<MediaDiscoveryService> logger)
    {
        ArgumentNullException.ThrowIfNull(libraryManager);
        ArgumentNullException.ThrowIfNull(userManager);
        ArgumentNullException.ThrowIfNull(libraryAccess);
        ArgumentNullException.ThrowIfNull(logger);

        _libraryManager = libraryManager;
        _userManager = userManager;
        _libraryAccess = libraryAccess;
        _logger = logger;
    }

    private static IReadOnlyList<DiscoveredMediaItem> Empty { get; } = Array.Empty<DiscoveredMediaItem>();

    /// <inheritdoc />
    public Task<IReadOnlyList<DiscoveredMediaItem>> DiscoverAsync(
        Guid userId,
        IReadOnlyList<MonitoredLibrary> monitoredLibraries,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(monitoredLibraries);
        cancellationToken.ThrowIfCancellationRequested();

        if (monitoredLibraries.Count == 0 || toUtc <= fromUtc)
        {
            return Task.FromResult(Empty);
        }

        var user = _userManager.GetUserById(userId);
        if (user is null)
        {
            _logger.LogWarning(
                "Media discovery requested for an unresolved Jellyfin user {UserId}; returning no candidates.",
                userId);
            return Task.FromResult(Empty);
        }

        cancellationToken.ThrowIfCancellationRequested();

        var accessibleLibraries = ResolveAccessibleLibraries(user, monitoredLibraries);
        if (accessibleLibraries.Count == 0)
        {
            return Task.FromResult(Empty);
        }

        var results = new List<DiscoveredMediaItem>();
        var seenIds = new HashSet<Guid>();

        // One query per accessible library (not per item), so every result is tagged with the
        // library it came from and that library's configured category.
        foreach (var (library, topParentIds) in accessibleLibraries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            IReadOnlyList<BaseItem> items;
            try
            {
                var query = new InternalItemsQuery(user)
                {
                    Recursive = true,
                    IsVirtualItem = false,
                    IncludeItemTypes = SupportedKinds,
                    TopParentIds = topParentIds,
                    MinDateCreated = fromUtc.UtcDateTime,
                    DtoOptions = new DtoOptions(false)
                };

                items = _libraryManager.GetItemList(query);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Jellyfin failed while discovering media for user {UserId}.", userId);
                throw new MediaDiscoveryException($"Jellyfin failed while discovering media for user {userId}.", ex);
            }

            cancellationToken.ThrowIfCancellationRequested();

            foreach (var item in items)
            {
                // Jellyfin stores DateCreated in UTC; SpecifyKind guards against an Unspecified Kind
                // being misread as local time.
                var createdUtc = new DateTimeOffset(DateTime.SpecifyKind(item.DateCreated, DateTimeKind.Utc));
                if (createdUtc <= fromUtc || createdUtc > toUtc)
                {
                    continue;
                }

                if (!seenIds.Add(item.Id))
                {
                    continue;
                }

                var discovered = item switch
                {
                    Movie movie => Normalize(movie, createdUtc),
                    Episode episode => Normalize(episode, createdUtc),
                    MusicAlbum album => Normalize(album, createdUtc),
                    _ => null
                };

                if (discovered is not null)
                {
                    discovered.LibraryId = library.LibraryId;
                    discovered.Category = library.Category;
                    results.Add(discovered);
                }
            }
        }

        _logger.LogInformation(
            "Media discovery for user {UserId} found {Count} authorized candidate(s) between {FromUtc:O} and {ToUtc:O}.",
            userId,
            results.Count,
            fromUtc,
            toUtc);

        return Task.FromResult<IReadOnlyList<DiscoveredMediaItem>>(results);
    }

    private List<(MonitoredLibrary Library, Guid[] TopParentIds)> ResolveAccessibleLibraries(User user, IReadOnlyList<MonitoredLibrary> monitoredLibraries)
    {
        var accessible = new List<(MonitoredLibrary, Guid[])>();
        var seenLibraries = new HashSet<Guid>();

        foreach (var library in monitoredLibraries)
        {
            var libraryId = library.LibraryId;

            // A library listed twice keeps its first entry (and so its first category).
            if (!seenLibraries.Add(libraryId))
            {
                continue;
            }

            LibraryAccess access;
            try
            {
                access = _libraryAccess.Resolve(user, libraryId);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Jellyfin failed while resolving library {LibraryId} for user {UserId}.", libraryId, user.Id);
                throw new MediaDiscoveryException($"Jellyfin failed while resolving library {libraryId}.", ex);
            }

            switch (access.Status)
            {
                case LibraryAccessStatus.Accessible:
                    var topParentIds = access.TopParentIds.Where(id => id != Guid.Empty).Distinct().ToArray();
                    if (topParentIds.Length > 0)
                    {
                        accessible.Add((library, topParentIds));
                    }

                    break;
                case LibraryAccessStatus.NotFound:
                case LibraryAccessStatus.NotLibrary:
                    _logger.LogWarning(
                        "Monitored library {LibraryId} is {Status}; skipping it. Review the monitored-library configuration.",
                        libraryId,
                        access.Status);
                    break;
                default:
                    _logger.LogDebug("Monitored library {LibraryId} is not accessible to user {UserId}; skipping it.", libraryId, user.Id);
                    break;
            }
        }

        return accessible;
    }

    private static DiscoveredMediaItem Normalize(Movie movie, DateTimeOffset createdUtc)
    {
        return new DiscoveredMediaItem
        {
            ItemId = movie.Id,
            ItemType = DiscoveredMediaItemType.Movie,
            Name = movie.Name ?? string.Empty,
            ProductionYear = movie.ProductionYear,
            DateAddedUtc = createdUtc
        };
    }

    private static DiscoveredMediaItem Normalize(Episode episode, DateTimeOffset createdUtc)
    {
        return new DiscoveredMediaItem
        {
            ItemId = episode.Id,
            ItemType = DiscoveredMediaItemType.Episode,
            Name = episode.Name ?? string.Empty,
            ProductionYear = episode.ProductionYear,
            DateAddedUtc = createdUtc,
            SeriesId = episode.SeriesId == Guid.Empty ? null : episode.SeriesId,
            SeriesName = string.IsNullOrEmpty(episode.SeriesName) ? null : episode.SeriesName,
            SeasonNumber = episode.ParentIndexNumber,
            EpisodeNumber = episode.IndexNumber
        };
    }

    private static DiscoveredMediaItem Normalize(MusicAlbum album, DateTimeOffset createdUtc)
    {
        return new DiscoveredMediaItem
        {
            ItemId = album.Id,
            ItemType = DiscoveredMediaItemType.MusicAlbum,
            Name = album.Name ?? string.Empty,
            ProductionYear = album.ProductionYear,
            DateAddedUtc = createdUtc,
            AlbumArtist = string.IsNullOrEmpty(album.AlbumArtist) ? null : album.AlbumArtist
        };
    }
}
