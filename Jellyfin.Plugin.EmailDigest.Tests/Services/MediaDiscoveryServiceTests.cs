using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Database.Implementations.Entities;
using Jellyfin.Plugin.EmailDigest.Configuration;
using Jellyfin.Plugin.EmailDigest.Models;
using Jellyfin.Plugin.EmailDigest.Services;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Audio;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.EmailDigest.Tests.Services;

public sealed class MediaDiscoveryServiceTests
{
    private static readonly Guid LibraryId = Guid.NewGuid();
    private static readonly DateTimeOffset FromUtc = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset ToUtc = new(2026, 1, 2, 0, 0, 0, TimeSpan.Zero);

    private static User MakeUser(Guid id)
    {
        return new User("user-" + id, "Jellyfin-Authentication-Default", "Jellyfin-PasswordReset-Default") { Id = id };
    }

    private static List<MonitoredLibrary> MonitoredLibraries(params Guid[] libraryIds)
    {
        return libraryIds.Select(id => new MonitoredLibrary { LibraryId = id, Category = MediaCategory.Movies }).ToList();
    }

    private static Movie MakeMovie(Guid? id = null, DateTime? dateCreated = null, string name = "Movie", int? year = 2020)
    {
        return new Movie
        {
            Id = id ?? Guid.NewGuid(),
            Name = name,
            DateCreated = dateCreated ?? FromUtc.AddHours(1).UtcDateTime,
            ProductionYear = year
        };
    }

    private static (Mock<ILibraryManager> LibraryManager, Mock<IUserManager> UserManager, MediaDiscoveryService Service) CreateService()
    {
        var libraryManager = new Mock<ILibraryManager>();
        var userManager = new Mock<IUserManager>();
        var service = new MediaDiscoveryService(libraryManager.Object, userManager.Object, NullLogger<MediaDiscoveryService>.Instance);
        return (libraryManager, userManager, service);
    }

    // 1-5, 11: authorization plumbing — the fake ILibraryManager stands in for Jellyfin's own
    // user-aware filtering; production code adds no permission logic of its own.

    [Fact]
    public async Task DiscoverAsync_UserAuthorizedForItem_ItemReturned()
    {
        var (libraryManager, userManager, service) = CreateService();
        var user = MakeUser(Guid.NewGuid());
        var movie = MakeMovie(name: "Movie A");
        userManager.Setup(m => m.GetUserById(user.Id)).Returns(user);
        libraryManager.Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>())).Returns(new List<BaseItem> { movie });

        var result = await service.DiscoverAsync(user.Id, MonitoredLibraries(LibraryId), FromUtc, ToUtc, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(movie.Id, result[0].ItemId);
    }

    [Fact]
    public async Task DiscoverAsync_UserNotAuthorizedForItem_ItemExcluded()
    {
        // The fake library manager simulates Jellyfin's own filtering by simply never returning
        // Movie B for this user's query — the production service never sees it to filter itself.
        var (libraryManager, userManager, service) = CreateService();
        var user = MakeUser(Guid.NewGuid());
        userManager.Setup(m => m.GetUserById(user.Id)).Returns(user);
        libraryManager.Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>())).Returns(new List<BaseItem>());

        var result = await service.DiscoverAsync(user.Id, MonitoredLibraries(LibraryId), FromUtc, ToUtc, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task DiscoverAsync_GloballyMonitoredButUserLacksAccess_LibraryExcluded()
    {
        var (libraryManager, userManager, service) = CreateService();
        var user = MakeUser(Guid.NewGuid());
        userManager.Setup(m => m.GetUserById(user.Id)).Returns(user);
        // Jellyfin itself would omit items from a library the user can't see; the fake mirrors
        // that by returning nothing for this monitored-but-inaccessible library.
        libraryManager.Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>())).Returns(new List<BaseItem>());

        var result = await service.DiscoverAsync(user.Id, MonitoredLibraries(LibraryId), FromUtc, ToUtc, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task DiscoverAsync_UserHasAccessButLibraryNotMonitored_ItemExcluded()
    {
        var (libraryManager, userManager, service) = CreateService();
        var user = MakeUser(Guid.NewGuid());
        var unmonitoredLibraryId = Guid.NewGuid();
        userManager.Setup(m => m.GetUserById(user.Id)).Returns(user);
        libraryManager
            .Setup(m => m.GetItemList(It.Is<InternalItemsQuery>(q => q.TopParentIds.Contains(unmonitoredLibraryId))))
            .Returns(new List<BaseItem> { MakeMovie() });
        libraryManager
            .Setup(m => m.GetItemList(It.Is<InternalItemsQuery>(q => !q.TopParentIds.Contains(unmonitoredLibraryId))))
            .Returns(new List<BaseItem>());

        var result = await service.DiscoverAsync(user.Id, MonitoredLibraries(LibraryId), FromUtc, ToUtc, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task DiscoverAsync_InvalidUser_ReturnsEmptyWithoutQuerying()
    {
        var (libraryManager, userManager, service) = CreateService();
        var invalidUserId = Guid.NewGuid();
        userManager.Setup(m => m.GetUserById(invalidUserId)).Returns((User?)null);

        var result = await service.DiscoverAsync(invalidUserId, MonitoredLibraries(LibraryId), FromUtc, ToUtc, CancellationToken.None);

        Assert.Empty(result);
        libraryManager.Verify(m => m.GetItemList(It.IsAny<InternalItemsQuery>()), Times.Never);
    }

    [Fact]
    public async Task DiscoverAsync_DuplicateItemIdsReturnedByQuery_OneCandidate()
    {
        var (libraryManager, userManager, service) = CreateService();
        var user = MakeUser(Guid.NewGuid());
        var id = Guid.NewGuid();
        userManager.Setup(m => m.GetUserById(user.Id)).Returns(user);
        libraryManager
            .Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>()))
            .Returns(new List<BaseItem> { MakeMovie(id), MakeMovie(id) });

        var result = await service.DiscoverAsync(user.Id, MonitoredLibraries(LibraryId), FromUtc, ToUtc, CancellationToken.None);

        Assert.Single(result);
    }

    // 6-10: media classification — the discovery layer rejects unsupported types before
    // returning results, even beyond what the query's IncludeItemTypes already filters.

    [Fact]
    public async Task DiscoverAsync_Collection_Excluded()
    {
        var (libraryManager, userManager, service) = CreateService();
        var user = MakeUser(Guid.NewGuid());
        var boxSet = new BoxSet { Id = Guid.NewGuid(), Name = "The Godfather Collection", DateCreated = FromUtc.AddHours(1).UtcDateTime };
        userManager.Setup(m => m.GetUserById(user.Id)).Returns(user);
        libraryManager.Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>())).Returns(new List<BaseItem> { boxSet });

        var result = await service.DiscoverAsync(user.Id, MonitoredLibraries(LibraryId), FromUtc, ToUtc, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task DiscoverAsync_IndividualAudioTrack_Excluded()
    {
        var (libraryManager, userManager, service) = CreateService();
        var user = MakeUser(Guid.NewGuid());
        var track = new Audio { Id = Guid.NewGuid(), Name = "Track 1", DateCreated = FromUtc.AddHours(1).UtcDateTime };
        userManager.Setup(m => m.GetUserById(user.Id)).Returns(user);
        libraryManager.Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>())).Returns(new List<BaseItem> { track });

        var result = await service.DiscoverAsync(user.Id, MonitoredLibraries(LibraryId), FromUtc, ToUtc, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task DiscoverAsync_Episode_NormalizedCorrectly()
    {
        var (libraryManager, userManager, service) = CreateService();
        var user = MakeUser(Guid.NewGuid());
        var seriesId = Guid.NewGuid();
        var episode = new Episode
        {
            Id = Guid.NewGuid(),
            Name = "Pilot",
            DateCreated = FromUtc.AddHours(1).UtcDateTime,
            SeriesId = seriesId,
            SeriesName = "Some Show",
            ParentIndexNumber = 1,
            IndexNumber = 1
        };
        userManager.Setup(m => m.GetUserById(user.Id)).Returns(user);
        libraryManager.Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>())).Returns(new List<BaseItem> { episode });

        var result = await service.DiscoverAsync(user.Id, MonitoredLibraries(LibraryId), FromUtc, ToUtc, CancellationToken.None);

        var discovered = Assert.Single(result);
        Assert.Equal(DiscoveredMediaItemType.Episode, discovered.ItemType);
        Assert.Equal("Pilot", discovered.Name);
        Assert.Equal(seriesId, discovered.SeriesId);
        Assert.Equal("Some Show", discovered.SeriesName);
        Assert.Equal(1, discovered.SeasonNumber);
        Assert.Equal(1, discovered.EpisodeNumber);
    }

    [Fact]
    public async Task DiscoverAsync_MusicAlbum_NormalizedCorrectly()
    {
        var (libraryManager, userManager, service) = CreateService();
        var user = MakeUser(Guid.NewGuid());
        var album = new MusicAlbum
        {
            Id = Guid.NewGuid(),
            Name = "Some Album",
            DateCreated = FromUtc.AddHours(1).UtcDateTime,
            ProductionYear = 2024,
            AlbumArtists = new[] { "Some Artist" }
        };
        userManager.Setup(m => m.GetUserById(user.Id)).Returns(user);
        libraryManager.Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>())).Returns(new List<BaseItem> { album });

        var result = await service.DiscoverAsync(user.Id, MonitoredLibraries(LibraryId), FromUtc, ToUtc, CancellationToken.None);

        var discovered = Assert.Single(result);
        Assert.Equal(DiscoveredMediaItemType.MusicAlbum, discovered.ItemType);
        Assert.Equal("Some Album", discovered.Name);
        Assert.Equal(2024, discovered.ProductionYear);
        // Compared against the real Jellyfin-computed getter rather than a hardcoded literal,
        // since MusicAlbum.AlbumArtist is derived from AlbumArtists by Jellyfin itself.
        Assert.Equal(album.AlbumArtist, discovered.AlbumArtist);
    }

    [Fact]
    public async Task DiscoverAsync_UnsupportedType_Excluded()
    {
        var (libraryManager, userManager, service) = CreateService();
        var user = MakeUser(Guid.NewGuid());
        var series = new Series { Id = Guid.NewGuid(), Name = "Some Show", DateCreated = FromUtc.AddHours(1).UtcDateTime };
        userManager.Setup(m => m.GetUserById(user.Id)).Returns(user);
        libraryManager.Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>())).Returns(new List<BaseItem> { series });

        var result = await service.DiscoverAsync(user.Id, MonitoredLibraries(LibraryId), FromUtc, ToUtc, CancellationToken.None);

        Assert.Empty(result);
    }

    // 12-16: boundary semantics — fromUtc < DateCreated <= toUtc.

    [Fact]
    public async Task DiscoverAsync_ItemBeforeFromUtc_Excluded()
    {
        var result = await DiscoverWithSingleItem(MakeMovie(dateCreated: FromUtc.AddMinutes(-1).UtcDateTime));
        Assert.Empty(result);
    }

    [Fact]
    public async Task DiscoverAsync_ItemExactlyAtFromUtc_Excluded()
    {
        var result = await DiscoverWithSingleItem(MakeMovie(dateCreated: FromUtc.UtcDateTime));
        Assert.Empty(result);
    }

    [Fact]
    public async Task DiscoverAsync_ItemInsideWindow_Included()
    {
        var result = await DiscoverWithSingleItem(MakeMovie(dateCreated: FromUtc.AddHours(12).UtcDateTime));
        Assert.Single(result);
    }

    [Fact]
    public async Task DiscoverAsync_ItemExactlyAtToUtc_Included()
    {
        var result = await DiscoverWithSingleItem(MakeMovie(dateCreated: ToUtc.UtcDateTime));
        Assert.Single(result);
    }

    [Fact]
    public async Task DiscoverAsync_ItemAfterToUtc_Excluded()
    {
        var result = await DiscoverWithSingleItem(MakeMovie(dateCreated: ToUtc.AddMinutes(1).UtcDateTime));
        Assert.Empty(result);
    }

    // 17-18: robustness.

    [Fact]
    public async Task DiscoverAsync_MissingOptionalMetadata_HandledWithoutException()
    {
        var episode = new Episode
        {
            Id = Guid.NewGuid(),
            Name = "Untitled",
            DateCreated = FromUtc.AddHours(1).UtcDateTime,
            ProductionYear = null,
            SeriesName = null
        };

        var result = await DiscoverWithSingleItem(episode);

        var discovered = Assert.Single(result);
        Assert.Null(discovered.ProductionYear);
        Assert.Null(discovered.SeriesId);
        Assert.Null(discovered.SeriesName);
        Assert.Null(discovered.SeasonNumber);
        Assert.Null(discovered.EpisodeNumber);
    }

    [Fact]
    public async Task DiscoverAsync_CancelledToken_ThrowsOperationCanceledException()
    {
        var (_, _, service) = CreateService();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.DiscoverAsync(Guid.NewGuid(), MonitoredLibraries(LibraryId), FromUtc, ToUtc, cts.Token));
    }

    // Cross-user test: the same candidate set, filtered entirely by the fake ILibraryManager
    // per-user — proving user-scoping without a second permission map in production code.

    [Fact]
    public async Task DiscoverAsync_SameCandidatesDifferentUsers_ProducesDifferentAuthorizedResults()
    {
        var (libraryManager, userManager, service) = CreateService();
        var user1 = MakeUser(Guid.NewGuid());
        var user2 = MakeUser(Guid.NewGuid());
        var movieA = MakeMovie(name: "Movie A");
        var episodeB = new Episode { Id = Guid.NewGuid(), Name = "Episode B", DateCreated = FromUtc.AddHours(1).UtcDateTime };
        var animeEpisodeC = new Episode { Id = Guid.NewGuid(), Name = "Anime Episode C", DateCreated = FromUtc.AddHours(1).UtcDateTime };
        var albumD = new MusicAlbum { Id = Guid.NewGuid(), Name = "Album D", DateCreated = FromUtc.AddHours(1).UtcDateTime };

        userManager.Setup(m => m.GetUserById(user1.Id)).Returns(user1);
        userManager.Setup(m => m.GetUserById(user2.Id)).Returns(user2);

        // Simulates what real Jellyfin's own query would return per user: user1 is authorized
        // for everything, user2 only for Movies/Series.
        libraryManager
            .Setup(m => m.GetItemList(It.Is<InternalItemsQuery>(q => q.User!.Id == user1.Id)))
            .Returns(new List<BaseItem> { movieA, episodeB, animeEpisodeC, albumD });
        libraryManager
            .Setup(m => m.GetItemList(It.Is<InternalItemsQuery>(q => q.User!.Id == user2.Id)))
            .Returns(new List<BaseItem> { movieA, episodeB });

        var monitoredLibraries = MonitoredLibraries(LibraryId);
        var resultUser1 = await service.DiscoverAsync(user1.Id, monitoredLibraries, FromUtc, ToUtc, CancellationToken.None);
        var resultUser2 = await service.DiscoverAsync(user2.Id, monitoredLibraries, FromUtc, ToUtc, CancellationToken.None);

        Assert.Equal(4, resultUser1.Count);
        Assert.Equal(new[] { movieA.Id, episodeB.Id, animeEpisodeC.Id, albumD.Id }, resultUser1.Select(i => i.ItemId));

        Assert.Equal(2, resultUser2.Count);
        Assert.Equal(new[] { movieA.Id, episodeB.Id }, resultUser2.Select(i => i.ItemId));
    }

    private static async Task<IReadOnlyList<DiscoveredMediaItem>> DiscoverWithSingleItem(BaseItem item)
    {
        var (libraryManager, userManager, service) = CreateService();
        var user = MakeUser(Guid.NewGuid());
        userManager.Setup(m => m.GetUserById(user.Id)).Returns(user);
        libraryManager.Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>())).Returns(new List<BaseItem> { item });

        return await service.DiscoverAsync(user.Id, MonitoredLibraries(LibraryId), FromUtc, ToUtc, CancellationToken.None);
    }
}
