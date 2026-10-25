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

    // Default resolver: every library is accessible and its items are stored under a
    // TopParentId equal to the library id, so tests not about access can ignore the seam.
    private static Mock<ILibraryAccessResolver> AllAccessibleResolver()
    {
        var resolver = new Mock<ILibraryAccessResolver>();
        resolver
            .Setup(r => r.Resolve(It.IsAny<User>(), It.IsAny<Guid>()))
            .Returns((User _, Guid id) => new LibraryAccess(LibraryAccessStatus.Accessible, new[] { id }));
        return resolver;
    }

    private static (Mock<ILibraryManager> LibraryManager, Mock<IUserManager> UserManager, MediaDiscoveryService Service) CreateService()
    {
        var (libraryManager, userManager, _, service) = CreateServiceWithResolver(AllAccessibleResolver());
        return (libraryManager, userManager, service);
    }

    private static (Mock<ILibraryManager> LibraryManager, Mock<IUserManager> UserManager, Mock<ILibraryAccessResolver> Resolver, MediaDiscoveryService Service) CreateServiceWithResolver(
        Mock<ILibraryAccessResolver> resolver)
    {
        var libraryManager = new Mock<ILibraryManager>();
        var userManager = new Mock<IUserManager>();
        var service = new MediaDiscoveryService(
            libraryManager.Object,
            userManager.Object,
            resolver.Object,
            NullLogger<MediaDiscoveryService>.Instance);
        return (libraryManager, userManager, resolver, service);
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
    public async Task DiscoverAsync_GloballyMonitoredButUserLacksAccess_NoQueryIsRun()
    {
        var resolver = new Mock<ILibraryAccessResolver>();
        resolver
            .Setup(r => r.Resolve(It.IsAny<User>(), LibraryId))
            .Returns(new LibraryAccess(LibraryAccessStatus.NotAccessible, Array.Empty<Guid>()));
        var (libraryManager, userManager, _, service) = CreateServiceWithResolver(resolver);
        var user = MakeUser(Guid.NewGuid());
        userManager.Setup(m => m.GetUserById(user.Id)).Returns(user);

        var result = await service.DiscoverAsync(user.Id, MonitoredLibraries(LibraryId), FromUtc, ToUtc, CancellationToken.None);

        Assert.Empty(result);
        // Never fall through to a query: with no TopParentIds Jellyfin would search every library.
        libraryManager.Verify(m => m.GetItemList(It.IsAny<InternalItemsQuery>()), Times.Never);
    }

    [Theory]
    [InlineData(LibraryAccessStatus.NotFound)]
    [InlineData(LibraryAccessStatus.NotLibrary)]
    [InlineData(LibraryAccessStatus.NotAccessible)]
    public async Task DiscoverAsync_NoMonitoredLibraryResolvable_ReturnsEmptyWithoutQuerying(LibraryAccessStatus status)
    {
        var resolver = new Mock<ILibraryAccessResolver>();
        resolver
            .Setup(r => r.Resolve(It.IsAny<User>(), It.IsAny<Guid>()))
            .Returns(new LibraryAccess(status, Array.Empty<Guid>()));
        var (libraryManager, userManager, _, service) = CreateServiceWithResolver(resolver);
        var user = MakeUser(Guid.NewGuid());
        userManager.Setup(m => m.GetUserById(user.Id)).Returns(user);

        var result = await service.DiscoverAsync(user.Id, MonitoredLibraries(LibraryId, Guid.NewGuid()), FromUtc, ToUtc, CancellationToken.None);

        Assert.Empty(result);
        libraryManager.Verify(m => m.GetItemList(It.IsAny<InternalItemsQuery>()), Times.Never);
    }

    [Fact]
    public async Task DiscoverAsync_MixedLibraries_QueriesOnlyAccessibleOnes()
    {
        var accessibleLibrary = Guid.NewGuid();
        var accessibleTopParent = Guid.NewGuid();
        var deniedLibrary = Guid.NewGuid();
        var deletedLibrary = Guid.NewGuid();
        var resolver = new Mock<ILibraryAccessResolver>();
        resolver
            .Setup(r => r.Resolve(It.IsAny<User>(), accessibleLibrary))
            .Returns(new LibraryAccess(LibraryAccessStatus.Accessible, new[] { accessibleTopParent }));
        resolver
            .Setup(r => r.Resolve(It.IsAny<User>(), deniedLibrary))
            .Returns(new LibraryAccess(LibraryAccessStatus.NotAccessible, Array.Empty<Guid>()));
        resolver
            .Setup(r => r.Resolve(It.IsAny<User>(), deletedLibrary))
            .Returns(new LibraryAccess(LibraryAccessStatus.NotFound, Array.Empty<Guid>()));
        var (libraryManager, userManager, _, service) = CreateServiceWithResolver(resolver);
        var user = MakeUser(Guid.NewGuid());
        userManager.Setup(m => m.GetUserById(user.Id)).Returns(user);
        InternalItemsQuery? captured = null;
        libraryManager
            .Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>()))
            .Callback<InternalItemsQuery>(q => captured = q)
            .Returns(new List<BaseItem> { MakeMovie() });

        var result = await service.DiscoverAsync(
            user.Id,
            MonitoredLibraries(accessibleLibrary, deniedLibrary, deletedLibrary),
            FromUtc,
            ToUtc,
            CancellationToken.None);

        Assert.Single(result);
        Assert.NotNull(captured);
        Assert.Equal(new[] { accessibleTopParent }, captured!.TopParentIds);
        Assert.Same(user, captured.User);
    }

    [Fact]
    public async Task DiscoverAsync_SameLibraryMonitoredTwice_ResolvedAndQueriedOnce()
    {
        var resolver = AllAccessibleResolver();
        var (libraryManager, userManager, _, service) = CreateServiceWithResolver(resolver);
        var user = MakeUser(Guid.NewGuid());
        userManager.Setup(m => m.GetUserById(user.Id)).Returns(user);
        InternalItemsQuery? captured = null;
        libraryManager
            .Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>()))
            .Callback<InternalItemsQuery>(q => captured = q)
            .Returns(new List<BaseItem>());

        await service.DiscoverAsync(user.Id, MonitoredLibraries(LibraryId, LibraryId), FromUtc, ToUtc, CancellationToken.None);

        resolver.Verify(r => r.Resolve(user, LibraryId), Times.Once);
        Assert.Equal(new[] { LibraryId }, captured!.TopParentIds);
    }

    [Fact]
    public async Task DiscoverAsync_EmptyMonitoredLibraries_ReturnsEmptyWithoutQuerying()
    {
        var (libraryManager, userManager, resolver, service) = CreateServiceWithResolver(AllAccessibleResolver());
        var user = MakeUser(Guid.NewGuid());
        userManager.Setup(m => m.GetUserById(user.Id)).Returns(user);

        var result = await service.DiscoverAsync(user.Id, MonitoredLibraries(), FromUtc, ToUtc, CancellationToken.None);

        Assert.Empty(result);
        libraryManager.Verify(m => m.GetItemList(It.IsAny<InternalItemsQuery>()), Times.Never);
        resolver.Verify(r => r.Resolve(It.IsAny<User>(), It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task DiscoverAsync_DifferentUsers_AccessResolvedPerUser()
    {
        var restrictedLibrary = Guid.NewGuid();
        var openUser = MakeUser(Guid.NewGuid());
        var restrictedUser = MakeUser(Guid.NewGuid());
        var resolver = new Mock<ILibraryAccessResolver>();
        resolver
            .Setup(r => r.Resolve(It.Is<User>(u => u.Id == openUser.Id), restrictedLibrary))
            .Returns(new LibraryAccess(LibraryAccessStatus.Accessible, new[] { restrictedLibrary }));
        resolver
            .Setup(r => r.Resolve(It.Is<User>(u => u.Id == restrictedUser.Id), restrictedLibrary))
            .Returns(new LibraryAccess(LibraryAccessStatus.NotAccessible, Array.Empty<Guid>()));
        var (libraryManager, userManager, _, service) = CreateServiceWithResolver(resolver);
        userManager.Setup(m => m.GetUserById(openUser.Id)).Returns(openUser);
        userManager.Setup(m => m.GetUserById(restrictedUser.Id)).Returns(restrictedUser);
        libraryManager.Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>())).Returns(new List<BaseItem> { MakeMovie() });

        var openResult = await service.DiscoverAsync(openUser.Id, MonitoredLibraries(restrictedLibrary), FromUtc, ToUtc, CancellationToken.None);
        var restrictedResult = await service.DiscoverAsync(restrictedUser.Id, MonitoredLibraries(restrictedLibrary), FromUtc, ToUtc, CancellationToken.None);

        Assert.Single(openResult);
        Assert.Empty(restrictedResult);
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

    // Category tagging: the category comes only from the configured library mapping.

    private static Episode MakeEpisode(string name = "Pilot")
    {
        return new Episode { Id = Guid.NewGuid(), Name = name, DateCreated = FromUtc.AddHours(1).UtcDateTime, SeriesName = "Some Show" };
    }

    [Fact]
    public async Task DiscoverAsync_EpisodeFromAnimeLibrary_ClassifiedAsAnimeNotSeries()
    {
        var animeLibrary = Guid.NewGuid();
        var (libraryManager, userManager, service) = CreateService();
        var user = MakeUser(Guid.NewGuid());
        userManager.Setup(m => m.GetUserById(user.Id)).Returns(user);
        libraryManager.Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>())).Returns(new List<BaseItem> { MakeEpisode() });
        var libraries = new List<MonitoredLibrary> { new() { LibraryId = animeLibrary, Category = MediaCategory.Anime } };

        var result = await service.DiscoverAsync(user.Id, libraries, FromUtc, ToUtc, CancellationToken.None);

        var item = Assert.Single(result);
        Assert.Equal(DiscoveredMediaItemType.Episode, item.ItemType);
        Assert.Equal(MediaCategory.Anime, item.Category);
        Assert.NotEqual(MediaCategory.Series, item.Category);
        Assert.Equal(animeLibrary, item.LibraryId);
    }

    [Fact]
    public async Task DiscoverAsync_SameItemTypeInDifferentLibraries_TaggedWithEachLibrarysCategory()
    {
        var seriesLibrary = Guid.NewGuid();
        var animeLibrary = Guid.NewGuid();
        var seriesEpisode = MakeEpisode("Series ep");
        var animeEpisode = MakeEpisode("Anime ep");
        var (libraryManager, userManager, service) = CreateService();
        var user = MakeUser(Guid.NewGuid());
        userManager.Setup(m => m.GetUserById(user.Id)).Returns(user);
        // The default fake resolver maps each library to a TopParentId equal to its own id.
        libraryManager
            .Setup(m => m.GetItemList(It.Is<InternalItemsQuery>(q => q.TopParentIds.Contains(seriesLibrary))))
            .Returns(new List<BaseItem> { seriesEpisode });
        libraryManager
            .Setup(m => m.GetItemList(It.Is<InternalItemsQuery>(q => q.TopParentIds.Contains(animeLibrary))))
            .Returns(new List<BaseItem> { animeEpisode });
        var libraries = new List<MonitoredLibrary>
        {
            new() { LibraryId = seriesLibrary, Category = MediaCategory.Series },
            new() { LibraryId = animeLibrary, Category = MediaCategory.Anime }
        };

        var result = await service.DiscoverAsync(user.Id, libraries, FromUtc, ToUtc, CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Equal(MediaCategory.Series, result.Single(i => i.ItemId == seriesEpisode.Id).Category);
        Assert.Equal(MediaCategory.Anime, result.Single(i => i.ItemId == animeEpisode.Id).Category);
        libraryManager.Verify(m => m.GetItemList(It.IsAny<InternalItemsQuery>()), Times.Exactly(2));
    }

    [Theory]
    [InlineData(MediaCategory.Documentaries)]
    [InlineData(MediaCategory.Other)]
    [InlineData(MediaCategory.Music)]
    public async Task DiscoverAsync_ConfiguredCategory_IsCopiedToItems(MediaCategory category)
    {
        var (libraryManager, userManager, service) = CreateService();
        var user = MakeUser(Guid.NewGuid());
        userManager.Setup(m => m.GetUserById(user.Id)).Returns(user);
        libraryManager.Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>())).Returns(new List<BaseItem> { MakeMovie() });
        var libraries = new List<MonitoredLibrary> { new() { LibraryId = LibraryId, Category = category } };

        var result = await service.DiscoverAsync(user.Id, libraries, FromUtc, ToUtc, CancellationToken.None);

        Assert.Equal(category, Assert.Single(result).Category);
    }

    [Fact]
    public async Task DiscoverAsync_ItemReturnedForTwoLibraries_KeptOnceUnderFirstConfiguredLibrary()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var movie = MakeMovie();
        var (libraryManager, userManager, service) = CreateService();
        var user = MakeUser(Guid.NewGuid());
        userManager.Setup(m => m.GetUserById(user.Id)).Returns(user);
        libraryManager.Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>())).Returns(new List<BaseItem> { movie });
        var libraries = new List<MonitoredLibrary>
        {
            new() { LibraryId = first, Category = MediaCategory.Movies },
            new() { LibraryId = second, Category = MediaCategory.Documentaries }
        };

        var result = await service.DiscoverAsync(user.Id, libraries, FromUtc, ToUtc, CancellationToken.None);

        var item = Assert.Single(result);
        Assert.Equal(first, item.LibraryId);
        Assert.Equal(MediaCategory.Movies, item.Category);
    }

    [Fact]
    public async Task DiscoverAsync_SameLibraryConfiguredTwiceWithDifferentCategories_FirstEntryWins()
    {
        var (libraryManager, userManager, service) = CreateService();
        var user = MakeUser(Guid.NewGuid());
        userManager.Setup(m => m.GetUserById(user.Id)).Returns(user);
        libraryManager.Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>())).Returns(new List<BaseItem> { MakeMovie() });
        var libraries = new List<MonitoredLibrary>
        {
            new() { LibraryId = LibraryId, Category = MediaCategory.Documentaries },
            new() { LibraryId = LibraryId, Category = MediaCategory.Movies }
        };

        var result = await service.DiscoverAsync(user.Id, libraries, FromUtc, ToUtc, CancellationToken.None);

        Assert.Equal(MediaCategory.Documentaries, Assert.Single(result).Category);
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

    [Fact]
    public async Task DiscoverAsync_JellyfinQueryThrows_SurfacesMediaDiscoveryExceptionNotEmpty()
    {
        var (libraryManager, userManager, service) = CreateService();
        var user = MakeUser(Guid.NewGuid());
        userManager.Setup(m => m.GetUserById(user.Id)).Returns(user);
        var failure = new InvalidOperationException("database is locked");
        libraryManager.Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>())).Throws(failure);

        var ex = await Assert.ThrowsAsync<MediaDiscoveryException>(
            () => service.DiscoverAsync(user.Id, MonitoredLibraries(LibraryId), FromUtc, ToUtc, CancellationToken.None));

        Assert.Same(failure, ex.InnerException);
    }

    [Fact]
    public async Task DiscoverAsync_LibraryResolutionThrows_SurfacesMediaDiscoveryException()
    {
        var resolver = new Mock<ILibraryAccessResolver>();
        resolver.Setup(r => r.Resolve(It.IsAny<User>(), It.IsAny<Guid>())).Throws(new InvalidOperationException("boom"));
        var (libraryManager, userManager, _, service) = CreateServiceWithResolver(resolver);
        var user = MakeUser(Guid.NewGuid());
        userManager.Setup(m => m.GetUserById(user.Id)).Returns(user);

        await Assert.ThrowsAsync<MediaDiscoveryException>(
            () => service.DiscoverAsync(user.Id, MonitoredLibraries(LibraryId), FromUtc, ToUtc, CancellationToken.None));

        libraryManager.Verify(m => m.GetItemList(It.IsAny<InternalItemsQuery>()), Times.Never);
    }

    [Fact]
    public async Task DiscoverAsync_QueryThrowsOperationCanceled_PropagatesUnwrapped()
    {
        var (libraryManager, userManager, service) = CreateService();
        var user = MakeUser(Guid.NewGuid());
        userManager.Setup(m => m.GetUserById(user.Id)).Returns(user);
        libraryManager.Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>())).Throws(new OperationCanceledException());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.DiscoverAsync(user.Id, MonitoredLibraries(LibraryId), FromUtc, ToUtc, CancellationToken.None));
    }

    [Fact]
    public async Task DiscoverAsync_CancelledDuringQuery_ThrowsAndDiscardsResults()
    {
        var (libraryManager, userManager, service) = CreateService();
        var user = MakeUser(Guid.NewGuid());
        using var cts = new CancellationTokenSource();
        userManager.Setup(m => m.GetUserById(user.Id)).Returns(user);
        libraryManager
            .Setup(m => m.GetItemList(It.IsAny<InternalItemsQuery>()))
            .Callback<InternalItemsQuery>(_ => cts.Cancel())
            .Returns(new List<BaseItem> { MakeMovie() });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.DiscoverAsync(user.Id, MonitoredLibraries(LibraryId), FromUtc, ToUtc, cts.Token));
    }

    [Fact]
    public async Task DiscoverAsync_EmptyOrInvertedWindow_ReturnsEmptyWithoutQuerying()
    {
        var (libraryManager, userManager, service) = CreateService();
        var user = MakeUser(Guid.NewGuid());
        userManager.Setup(m => m.GetUserById(user.Id)).Returns(user);

        var empty = await service.DiscoverAsync(user.Id, MonitoredLibraries(LibraryId), FromUtc, FromUtc, CancellationToken.None);
        var inverted = await service.DiscoverAsync(user.Id, MonitoredLibraries(LibraryId), ToUtc, FromUtc, CancellationToken.None);

        Assert.Empty(empty);
        Assert.Empty(inverted);
        libraryManager.Verify(m => m.GetItemList(It.IsAny<InternalItemsQuery>()), Times.Never);
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
