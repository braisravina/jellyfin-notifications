using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Enums;
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
/// Authorization comes entirely from Jellyfin: a <see cref="MediaBrowser.Controller.Entities.InternalItemsQuery"/>
/// is built with its <c>User</c> set to the resolved <see cref="MediaBrowser.Controller.Library.IUserManager"/>
/// user, and <see cref="MediaBrowser.Controller.Library.ILibraryManager.GetItemList(InternalItemsQuery)"/> is
/// trusted to only return items that user can already see — the same mechanism Jellyfin's own API
/// controllers use. This class never inspects or duplicates library-access or parental-rating
/// state itself. An unresolved user, an empty monitored-library list, or an empty/inverted window
/// short-circuit to an empty result without ever running an unrestricted query.
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
    private readonly ILogger<MediaDiscoveryService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="MediaDiscoveryService"/> class.
    /// </summary>
    /// <param name="libraryManager">Jellyfin's library manager.</param>
    /// <param name="userManager">Jellyfin's user manager.</param>
    /// <param name="logger">Logger.</param>
    public MediaDiscoveryService(ILibraryManager libraryManager, IUserManager userManager, ILogger<MediaDiscoveryService> logger)
    {
        ArgumentNullException.ThrowIfNull(libraryManager);
        ArgumentNullException.ThrowIfNull(userManager);
        ArgumentNullException.ThrowIfNull(logger);

        _libraryManager = libraryManager;
        _userManager = userManager;
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

        var query = new InternalItemsQuery(user)
        {
            Recursive = true,
            IsVirtualItem = false,
            IncludeItemTypes = SupportedKinds,
            TopParentIds = monitoredLibraries.Select(library => library.LibraryId).Distinct().ToArray(),
            MinDateCreated = fromUtc.UtcDateTime,
            DtoOptions = new DtoOptions(false)
        };

        var items = _libraryManager.GetItemList(query);

        cancellationToken.ThrowIfCancellationRequested();

        var results = new List<DiscoveredMediaItem>();
        var seenIds = new HashSet<Guid>();

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
                results.Add(discovered);
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
