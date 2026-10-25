using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.EmailDigest.Configuration;
using Jellyfin.Plugin.EmailDigest.Digest;
using Jellyfin.Plugin.EmailDigest.Models;
using Xunit;

namespace Jellyfin.Plugin.EmailDigest.Tests.Digest;

public sealed class DigestBuilderTests
{
    private static readonly DigestBuilder Builder = new();

    private static int _counter;

    private static Guid NextId()
    {
        // Deterministic ids so ordering assertions are stable across runs.
        return new Guid(System.Threading.Interlocked.Increment(ref _counter), 0, 0, 0, 0, 0, 0, 0, 0, 0, 1);
    }

    private static DiscoveredMediaItem Movie(string name, MediaCategory category = MediaCategory.Movies, int? year = 2020, Guid? id = null)
    {
        return new DiscoveredMediaItem
        {
            ItemId = id ?? NextId(),
            ItemType = DiscoveredMediaItemType.Movie,
            Name = name,
            ProductionYear = year,
            Category = category
        };
    }

    private static DiscoveredMediaItem Episode(
        string series,
        int? season,
        int? number,
        string name = "Ep",
        MediaCategory category = MediaCategory.Series,
        Guid? seriesId = null)
    {
        return new DiscoveredMediaItem
        {
            ItemId = NextId(),
            ItemType = DiscoveredMediaItemType.Episode,
            Name = name,
            SeriesName = series,
            SeriesId = seriesId,
            SeasonNumber = season,
            EpisodeNumber = number,
            Category = category
        };
    }

    private static DiscoveredMediaItem Album(string name, string? artist = "Artist")
    {
        return new DiscoveredMediaItem
        {
            ItemId = NextId(),
            ItemType = DiscoveredMediaItemType.MusicAlbum,
            Name = name,
            AlbumArtist = artist,
            Category = MediaCategory.Music
        };
    }

    [Fact]
    public void Build_NoItems_IsEmptyWithNoSections()
    {
        var model = Builder.Build(Array.Empty<DiscoveredMediaItem>(), DigestOptions.Default);

        Assert.True(model.IsEmpty);
        Assert.Empty(model.Sections);
        Assert.Empty(model.Items);
    }

    [Fact]
    public void Build_MixedCategories_SectionsInFixedOrderSkippingEmptyOnes()
    {
        var items = new[]
        {
            Album("Album"),
            Movie("Doc", MediaCategory.Documentaries),
            Episode("Anime Show", 1, 1, category: MediaCategory.Anime),
            Movie("Film"),
            Movie("Misc", MediaCategory.Other),
            Episode("Show", 1, 1)
        };

        var model = Builder.Build(items, DigestOptions.Default);

        Assert.Equal(
            new[]
            {
                MediaCategory.Movies,
                MediaCategory.Series,
                MediaCategory.Anime,
                MediaCategory.Documentaries,
                MediaCategory.Music,
                MediaCategory.Other
            },
            model.Sections.Select(s => s.Category));
    }

    [Fact]
    public void Build_AnimeEpisodes_AreNotMergedIntoSeries()
    {
        var items = new[]
        {
            Episode("Same Name", 1, 1, category: MediaCategory.Series),
            Episode("Same Name", 1, 2, category: MediaCategory.Anime)
        };

        var model = Builder.Build(items, DigestOptions.Default);

        Assert.Equal(2, model.Sections.Count);
        Assert.All(model.Sections, s => Assert.Single(s.Entries));
    }

    [Fact]
    public void Build_MultipleEpisodesOfOneSeries_GroupedAndOrdered()
    {
        var seriesId = Guid.NewGuid();
        var items = new[]
        {
            Episode("Show", 2, 1, "S2E1", seriesId: seriesId),
            Episode("Show", 1, 2, "S1E2", seriesId: seriesId),
            Episode("Show", 1, 1, "S1E1", seriesId: seriesId),
            Episode("Show", null, null, "Special", seriesId: seriesId)
        };

        var entry = Assert.Single(Assert.Single(Builder.Build(items, DigestOptions.Default).Sections).Entries);

        Assert.Equal(DigestEntryKind.Series, entry.Kind);
        Assert.Equal("Show", entry.Title);
        Assert.Equal(new[] { "S1E1", "S1E2", "S2E1", "Special" }, entry.Episodes.Select(e => e.Title));
        Assert.Equal(4, entry.ItemIds.Count);
    }

    [Fact]
    public void Build_EpisodesWithoutSeriesId_GroupBySeriesNameCaseInsensitively()
    {
        var items = new[] { Episode("Show", 1, 1), Episode("SHOW", 1, 2), Episode("Other", 1, 1) };

        var entries = Assert.Single(Builder.Build(items, DigestOptions.Default).Sections).Entries;

        Assert.Equal(2, entries.Count);
        Assert.Equal(2, entries.Single(e => e.Title.Equals("Show", StringComparison.OrdinalIgnoreCase)).Episodes.Count);
    }

    [Fact]
    public void Build_EpisodesWithNoSeriesInfo_FormOneUnknownSeriesEntryWithEmptyTitle()
    {
        var items = new[] { Episode(string.Empty, null, null, "A"), Episode(string.Empty, null, null, "B") };

        var entry = Assert.Single(Assert.Single(Builder.Build(items, DigestOptions.Default).Sections).Entries);

        Assert.Equal(string.Empty, entry.Title);
        Assert.Equal(2, entry.Episodes.Count);
    }

    [Fact]
    public void Build_DuplicateItemIds_AppearOnce()
    {
        var id = Guid.NewGuid();
        var items = new[] { Movie("A", id: id), Movie("A", id: id), Movie("B") };

        var model = Builder.Build(items, DigestOptions.Default);

        Assert.Equal(2, model.Items.Count);
        Assert.Equal(2, model.Sections.Single().TotalEntryCount);
    }

    [Fact]
    public void Build_MissingMetadata_DoesNotThrowAndKeepsNulls()
    {
        var items = new[]
        {
            Movie(string.Empty, year: null),
            Album("Untitled album", artist: "  "),
            Episode(string.Empty, null, null, string.Empty)
        };

        var model = Builder.Build(items, DigestOptions.Default);

        Assert.Equal(3, model.Items.Count);
        var movie = model.Sections.First(s => s.Category == MediaCategory.Movies).Entries.Single();
        Assert.Null(movie.Year);
        var album = model.Sections.First(s => s.Category == MediaCategory.Music).Entries.Single();
        Assert.Null(album.Artist);
    }

    [Fact]
    public void Build_EntriesOrderedByTitleIgnoringCase()
    {
        var items = new[] { Movie("banana"), Movie("Apple"), Movie("cherry") };

        var titles = Builder.Build(items, DigestOptions.Default).Sections.Single().Entries.Select(e => e.Title);

        Assert.Equal(new[] { "Apple", "banana", "cherry" }, titles);
    }

    [Fact]
    public void Build_SameTitles_OrderedByIdSoOutputIsStable()
    {
        var a = Movie("Same");
        var b = Movie("Same");

        var forward = Builder.Build(new[] { a, b }, DigestOptions.Default);
        var reversed = Builder.Build(new[] { b, a }, DigestOptions.Default);

        Assert.Equal(
            forward.Sections.Single().Entries.Select(e => e.ItemIds[0]),
            reversed.Sections.Single().Entries.Select(e => e.ItemIds[0]));
    }

    [Fact]
    public void Build_InputOrderDoesNotMatter()
    {
        var items = new List<DiscoveredMediaItem>
        {
            Movie("B"), Movie("A"), Album("X"), Episode("S", 1, 2), Episode("S", 1, 1), Movie("C", MediaCategory.Documentaries)
        };

        var first = Builder.Build(items, DigestOptions.Default);
        items.Reverse();
        var second = Builder.Build(items, DigestOptions.Default);

        Assert.Equal(Snapshot(first), Snapshot(second));
    }

    [Fact]
    public void Build_SameInputTwice_SameOutput()
    {
        var items = new[] { Movie("A"), Episode("S", 1, 1) };

        Assert.Equal(Snapshot(Builder.Build(items, DigestOptions.Default)), Snapshot(Builder.Build(items, DigestOptions.Default)));
    }

    [Fact]
    public void Build_DoesNotMutateItsInput()
    {
        var items = new List<DiscoveredMediaItem> { Movie("B"), Movie("A") };
        var before = items.Select(i => (i.ItemId, i.Name, i.Category)).ToList();

        Builder.Build(items, DigestOptions.Default);

        Assert.Equal(before, items.Select(i => (i.ItemId, i.Name, i.Category)).ToList());
    }

    [Fact]
    public void Build_SectionLimit_HidesEntriesAndCountsThem()
    {
        var items = Enumerable.Range(1, 8).Select(i => Movie("Movie " + i)).ToList();

        var model = Builder.Build(items, new DigestOptions { MaxEntriesPerSection = 3 });

        var section = Assert.Single(model.Sections);
        Assert.Equal(3, section.Entries.Count);
        Assert.Equal(8, section.TotalEntryCount);
        Assert.Equal(5, section.HiddenEntryCount);
        Assert.Equal(5, section.HiddenItemCount);
        Assert.Equal(8, model.Items.Count);
    }

    [Fact]
    public void Build_HiddenSeries_CountsAllItsEpisodesAsHiddenItems()
    {
        var items = new List<DiscoveredMediaItem>
        {
            Episode("A Show", 1, 1),
            Episode("B Show", 1, 1),
            Episode("B Show", 1, 2),
            Episode("B Show", 1, 3)
        };

        var section = Assert.Single(Builder.Build(items, new DigestOptions { MaxEntriesPerSection = 1 }).Sections);

        Assert.Equal("A Show", Assert.Single(section.Entries).Title);
        Assert.Equal(1, section.HiddenEntryCount);
        Assert.Equal(3, section.HiddenItemCount);
        Assert.Equal(4, section.TotalItemCount);
    }

    [Fact]
    public void Build_DigestLimit_IsSharedFairly_ExtraEntriesGoToEarlierSections_NothingIsLost()
    {
        var items = new List<DiscoveredMediaItem>();
        items.AddRange(Enumerable.Range(1, 4).Select(i => Movie("Movie " + i)));
        items.AddRange(Enumerable.Range(1, 4).Select(i => Album("Album " + i)));

        var model = Builder.Build(items, new DigestOptions { MaxEntriesPerSection = 10, MaxEntriesPerDigest = 5 });

        Assert.Equal(3, model.Sections[0].Entries.Count);
        Assert.Equal(2, model.Sections[1].Entries.Count);
        Assert.Equal(1, model.Sections[0].HiddenEntryCount);
        Assert.Equal(2, model.Sections[1].HiddenEntryCount);
        Assert.Equal(8, model.Items.Count);
        Assert.Equal(8, model.Sections.Sum(s => s.TotalItemCount));
        Assert.True(model.HasHiddenEntries);
    }

    [Fact]
    public void Build_OneHugeCategory_CannotCrowdOutTheOthers()
    {
        var items = new List<DiscoveredMediaItem>();
        items.AddRange(Enumerable.Range(1, 300).Select(i => Movie("Movie " + i)));
        items.Add(Album("The only album"));
        items.Add(Movie("A documentary", MediaCategory.Documentaries));

        var model = Builder.Build(items, new DigestOptions { MaxEntriesPerSection = 200, MaxEntriesPerDigest = 10 });

        Assert.Single(model.Sections.Single(s => s.Category == MediaCategory.Music).Entries);
        Assert.Single(model.Sections.Single(s => s.Category == MediaCategory.Documentaries).Entries);
        Assert.Equal(8, model.Sections.Single(s => s.Category == MediaCategory.Movies).Entries.Count);
        Assert.Equal(10, model.Sections.Sum(s => s.Entries.Count));
    }

    [Fact]
    public void Build_SmallCategoriesFreeTheirUnusedShareForTheLargeOne()
    {
        var items = new List<DiscoveredMediaItem> { Album("A") };
        items.AddRange(Enumerable.Range(1, 50).Select(i => Movie("Movie " + i)));

        var model = Builder.Build(items, new DigestOptions { MaxEntriesPerSection = 100, MaxEntriesPerDigest = 20 });

        Assert.Equal(19, model.Sections.Single(s => s.Category == MediaCategory.Movies).Entries.Count);
        Assert.Single(model.Sections.Single(s => s.Category == MediaCategory.Music).Entries);
    }

    [Fact]
    public void Build_FewerSectionsThanBudget_EachSectionGetsAtLeastOne()
    {
        var items = new List<DiscoveredMediaItem> { Movie("M1"), Movie("M2"), Album("A1"), Album("A2"), Movie("O", MediaCategory.Other) };

        var model = Builder.Build(items, new DigestOptions { MaxEntriesPerSection = 10, MaxEntriesPerDigest = 3 });

        Assert.All(model.Sections, s => Assert.Single(s.Entries));
    }

    [Fact]
    public void Build_NothingHidden_HasNoHiddenEntries()
    {
        var model = Builder.Build(new[] { Movie("A"), Album("B") }, DigestOptions.Default);

        Assert.False(model.HasHiddenEntries);
    }

    [Fact]
    public void Build_Allocation_IsDeterministicWhateverTheInputOrder()
    {
        var items = new List<DiscoveredMediaItem>();
        items.AddRange(Enumerable.Range(1, 9).Select(i => Movie("Movie " + i)));
        items.AddRange(Enumerable.Range(1, 9).Select(i => Album("Album " + i)));
        items.AddRange(Enumerable.Range(1, 9).Select(i => Movie("Doc " + i, MediaCategory.Documentaries)));
        var options = new DigestOptions { MaxEntriesPerSection = 9, MaxEntriesPerDigest = 10 };

        var first = Builder.Build(items, options);
        items.Reverse();
        var second = Builder.Build(items, options);

        Assert.Equal(Snapshot(first), Snapshot(second));
        Assert.Equal(new[] { 4, 3, 3 }, first.Sections.Select(s => s.Entries.Count));
    }

    [Fact]
    public void Build_DigestBudgetExhausted_LaterSectionStillReportedWithZeroShown()
    {
        var items = new List<DiscoveredMediaItem> { Movie("A"), Album("B") };

        var model = Builder.Build(items, new DigestOptions { MaxEntriesPerDigest = 1 });

        var music = model.Sections.Single(s => s.Category == MediaCategory.Music);
        Assert.Empty(music.Entries);
        Assert.Equal(1, music.HiddenEntryCount);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    [InlineData(-1, 5)]
    public void Build_NonPositiveLimits_Throw(int perSection, int perDigest)
    {
        var options = new DigestOptions { MaxEntriesPerSection = perSection, MaxEntriesPerDigest = perDigest };

        Assert.Throws<ArgumentOutOfRangeException>(() => Builder.Build(new[] { Movie("A") }, options));
    }

    [Fact]
    public void Build_UnknownCategoryValue_ThrowsRatherThanDroppingItems()
    {
        var items = new[] { Movie("A", (MediaCategory)99) };

        Assert.Throws<InvalidOperationException>(() => Builder.Build(items, DigestOptions.Default));
    }

    [Fact]
    public void Build_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => Builder.Build(null!, DigestOptions.Default));
        Assert.Throws<ArgumentNullException>(() => Builder.Build(Array.Empty<DiscoveredMediaItem>(), null!));
    }

    private static string Snapshot(DigestModel model)
    {
        return string.Join(
            "|",
            model.Sections.Select(s => $"{s.Category}:{s.TotalEntryCount}:{string.Join(",", s.Entries.Select(e => $"{e.Kind}/{e.Title}/{string.Join(".", e.ItemIds)}"))}"));
    }
}
