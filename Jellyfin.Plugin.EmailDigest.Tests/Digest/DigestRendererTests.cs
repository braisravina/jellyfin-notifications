using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.EmailDigest.Configuration;
using Jellyfin.Plugin.EmailDigest.Digest;
using Jellyfin.Plugin.EmailDigest.Models;
using Jellyfin.Plugin.EmailDigest.Services;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.EmailDigest.Tests.Digest;

public sealed class DigestRendererTests
{
    private static readonly DigestRenderer Renderer = new(new LocalizationService());
    private static readonly DigestBuilder Builder = new();

    private static int _counter;

    private static Guid NextId()
    {
        return new Guid(System.Threading.Interlocked.Increment(ref _counter), 0, 0, 0, 0, 0, 0, 0, 0, 0, 2);
    }

    private static DiscoveredMediaItem Movie(string name, int? year = 2020, MediaCategory category = MediaCategory.Movies)
    {
        return new DiscoveredMediaItem { ItemId = NextId(), ItemType = DiscoveredMediaItemType.Movie, Name = name, ProductionYear = year, Category = category };
    }

    private static DiscoveredMediaItem Episode(string series, int? season, int? number, string name, MediaCategory category = MediaCategory.Series)
    {
        return new DiscoveredMediaItem
        {
            ItemId = NextId(),
            ItemType = DiscoveredMediaItemType.Episode,
            Name = name,
            SeriesName = series,
            SeasonNumber = season,
            EpisodeNumber = number,
            Category = category
        };
    }

    private static DiscoveredMediaItem Album(string name, string? artist)
    {
        return new DiscoveredMediaItem { ItemId = NextId(), ItemType = DiscoveredMediaItemType.MusicAlbum, Name = name, AlbumArtist = artist, Category = MediaCategory.Music };
    }

    private static RenderedDigest Render(IReadOnlyCollection<DiscoveredMediaItem> items, string? language = "en", string? name = "Ana", DigestOptions? options = null)
    {
        return Renderer.Render(Builder.Build(items, options ?? DigestOptions.Default), new DigestRecipient(name, language));
    }

    // Golden fixture: every category, grouping, overflow, missing data and special characters.
    internal static List<DiscoveredMediaItem> GoldenItems()
    {
        _counter = 1000;
        var items = new List<DiscoveredMediaItem>
        {
            Movie("Amélie", 2001),
            Movie("Tom & Jerry <The Movie>", 2021),
            Movie("Third Film", 2019),
            Movie("Zorro", 1998),
            Movie(string.Empty, null),
            Episode("Breaking Bad", 1, 1, "Pilot"),
            Episode("Breaking Bad", 1, 2, "Cat's in the Bag..."),
            Episode("Breaking Bad", 2, null, "Special"),
            Episode("Breaking Bad", null, 5, "Extra"),
            Episode("Breaking Bad", null, null, string.Empty),
            Episode("千と千尋の神隠し", 1, 1, "第1話", MediaCategory.Anime),
            Movie("Planet Earth", 2006, MediaCategory.Documentaries),
            Album("Kind of Blue", "Miles Davis"),
            Album("Untitled Demo", null),
            Movie("Home Video", 2020, MediaCategory.Other)
        };

        for (var i = 1; i <= 12; i++)
        {
            items.Add(Episode("Long Running Show", 1, i, "Episode " + i));
        }

        return items;
    }

    internal static RenderedDigest RenderGolden(string language)
    {
        return Renderer.Render(
            Builder.Build(GoldenItems(), new DigestOptions { MaxEntriesPerSection = 4 }),
            new DigestRecipient("Ana", language));
    }

    [Fact]
    public void Render_Movie_IncludesTitleAndYearInBothFormats()
    {
        var result = Render(new[] { Movie("Heat", 1995) });

        Assert.Contains("Heat (1995)", result.TextBody, StringComparison.Ordinal);
        Assert.Contains("<strong>Heat (1995)</strong>", result.HtmlBody, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_AllCategories_EachGetsItsLocalizedSectionInOrder()
    {
        var result = Render(new[]
        {
            Album("A", "B"), Movie("D", 2000, MediaCategory.Documentaries), Episode("S", 1, 1, "E", MediaCategory.Anime),
            Movie("M"), Movie("O", 2000, MediaCategory.Other), Episode("S2", 1, 1, "E")
        });

        var headings = new[] { "Movies", "Series", "Anime", "Documentaries", "Music", "Other" };
        var positions = headings.Select(h => result.TextBody.IndexOf("\n" + h + "\n", StringComparison.Ordinal)).ToList();
        Assert.All(positions, p => Assert.True(p >= 0));
        Assert.Equal(positions.OrderBy(p => p), positions);
        Assert.All(headings, h => Assert.Contains("<h2 ", result.HtmlBody.Substring(result.HtmlBody.IndexOf(">" + h + "</h2>", StringComparison.Ordinal) - 150), StringComparison.Ordinal));
    }

    [Fact]
    public void Render_SeriesEpisodes_ShowCountAndLabels()
    {
        var result = Render(new[] { Episode("Show", 1, 2, "Two"), Episode("Show", 1, 1, "One") });

        Assert.Contains("* Show — 2 new episodes", result.TextBody, StringComparison.Ordinal);
        Assert.True(
            result.TextBody.IndexOf("S01E01 – One", StringComparison.Ordinal) < result.TextBody.IndexOf("S01E02 – Two", StringComparison.Ordinal));
    }

    [Fact]
    public void Render_SingleEpisode_UsesSingular()
    {
        var result = Render(new[] { Episode("Show", 1, 1, "One") });

        Assert.Contains("1 new episode", result.TextBody, StringComparison.Ordinal);
        Assert.DoesNotContain("1 new episodes", result.TextBody, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_MissingMetadata_UsesLocalizedPlaceholders()
    {
        var result = Render(new[] { Movie(string.Empty, null), Episode(string.Empty, null, null, string.Empty), Album(string.Empty, null) });

        Assert.Contains("* Untitled", result.TextBody, StringComparison.Ordinal);
        Assert.Contains("* Unknown series", result.TextBody, StringComparison.Ordinal);
        Assert.DoesNotContain("()", result.TextBody, StringComparison.Ordinal);
        Assert.DoesNotContain("null", result.TextBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Render_EpisodeWithOnlySeasonOrOnlyNumber_UsesPartialLabels()
    {
        var result = Render(new[] { Episode("Show", 3, null, "Special"), Episode("Show", null, 4, "Extra") });

        Assert.Contains("Season 3 – Special", result.TextBody, StringComparison.Ordinal);
        Assert.Contains("Episode 4 – Extra", result.TextBody, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_SectionOverflow_ReportsAccurateCount()
    {
        var items = Enumerable.Range(1, 8).Select(i => Movie("Movie " + i)).ToList();

        var result = Render(items, options: new DigestOptions { MaxEntriesPerSection = 3 });

        Assert.Contains("and 5 more", result.TextBody, StringComparison.Ordinal);
        Assert.Contains("and 5 more", result.HtmlBody, StringComparison.Ordinal);
        Assert.Equal(3, Regex.Matches(result.TextBody, @"^\* ", RegexOptions.Multiline).Count);
    }

    [Fact]
    public void Render_ManyEpisodes_ListsTenThenOverflow()
    {
        var items = Enumerable.Range(1, 14).Select(i => Episode("Show", 1, i, "E" + i)).ToList();

        var result = Render(items);

        Assert.Contains("* Show — 14 new episodes", result.TextBody, StringComparison.Ordinal);
        Assert.Equal(DigestRenderer.MaxEpisodesListed, Regex.Matches(result.TextBody, @"^    - ", RegexOptions.Multiline).Count);
        Assert.Contains("and 4 more", result.TextBody, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_SubjectCountsEveryEligibleItemNotJustDisplayedOnes()
    {
        var items = Enumerable.Range(1, 8).Select(i => Movie("Movie " + i)).ToList();

        var result = Render(items, options: new DigestOptions { MaxEntriesPerSection = 2 });

        Assert.Equal("Jellyfin digest: 8 new items", result.Subject);
    }

    [Fact]
    public void Render_OneItem_SubjectIsSingular()
    {
        Assert.Equal("Jellyfin digest: 1 new item", Render(new[] { Movie("A") }).Subject);
    }

    [Theory]
    [InlineData("en", "Nothing new since your last digest.", "Your Jellyfin Digest")]
    [InlineData("es", "No hay novedades desde tu último resumen.", "Tu resumen de Jellyfin")]
    [InlineData("gl", "Non hai novidades dende o teu último resumo.", "O teu resumo de Jellyfin")]
    public void Render_EmptyDigest_SaysNothingNewInTheRecipientsLanguage(string language, string message, string subject)
    {
        var result = Render(Array.Empty<DiscoveredMediaItem>(), language);

        Assert.Equal(subject, result.Subject);
        Assert.Contains(message, result.TextBody, StringComparison.Ordinal);
        Assert.Contains(message, WebUtility.HtmlDecode(result.HtmlBody), StringComparison.Ordinal);
        Assert.DoesNotContain("<h2", result.HtmlBody, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en", "Hello Ana,")]
    [InlineData("es", "Hola Ana,")]
    [InlineData("gl", "Ola Ana,")]
    public void Render_Greeting_IsLocalized(string language, string expected)
    {
        Assert.StartsWith(expected, Render(new[] { Movie("A") }, language).TextBody, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_NoDisplayName_UsesNamelessGreeting()
    {
        Assert.StartsWith("Hello,\n", Render(new[] { Movie("A") }, name: "  ").TextBody, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("fr")]
    [InlineData("")]
    public void Render_UnsupportedLanguage_RendersExactlyLikeEnglish(string? language)
    {
        var items = new[] { Movie("A"), Album("B", "C") };

        var fallback = Render(items, language);
        var english = Render(items, "en");

        Assert.Equal(english, fallback);
        Assert.Contains("lang=\"en\"", fallback.HtmlBody, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en", "Documentaries")]
    [InlineData("es", "Documentales")]
    [InlineData("gl", "Documentais")]
    public void Render_HtmlLangAttributeAndHeadingsFollowLanguage(string language, string heading)
    {
        var result = Render(new[] { Movie("A", 2000, MediaCategory.Documentaries) }, language);

        Assert.Contains($"lang=\"{language}\"", result.HtmlBody, StringComparison.Ordinal);
        Assert.Contains(">" + heading + "</h2>", result.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("\n" + heading + "\n", result.TextBody, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_Unicode_SurvivesInBothFormats()
    {
        var titles = new[] { "Amélie", "千と千尋の神隠し", "Ñandú — 🎵", "Ελληνικά" };

        var result = Render(titles.Select(t => Movie(t)).ToList());

        foreach (var title in titles)
        {
            Assert.Contains(title, result.TextBody, StringComparison.Ordinal);
            Assert.Contains(title, WebUtility.HtmlDecode(result.HtmlBody), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Render_SameInputTwice_IsByteIdentical()
    {
        Assert.Equal(RenderGolden("es"), RenderGolden("es"));
    }

    [Fact]
    public void Render_InputOrderDoesNotChangeOutput()
    {
        var items = GoldenItems();
        var forward = Renderer.Render(Builder.Build(items, DigestOptions.Default), new DigestRecipient("Ana", "en"));
        items.Reverse();
        var reversed = Renderer.Render(Builder.Build(items, DigestOptions.Default), new DigestRecipient("Ana", "en"));

        Assert.Equal(forward, reversed);
    }

    // Security

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("\"><img src=x onerror=alert(1)>")]
    [InlineData("<a href=\"https://evil.example\">click</a>")]
    [InlineData("' onmouseover='alert(1)")]
    [InlineData("</title><body onload=alert(1)>")]
    [InlineData("<style>@import url(https://evil.example/x.css)</style>")]
    public void Render_HostileTitles_NeverProduceActiveMarkup(string hostile)
    {
        var items = new List<DiscoveredMediaItem>
        {
            Movie(hostile), Album(hostile, hostile), Episode(hostile, 1, 1, hostile), Movie(hostile, 2000, MediaCategory.Other)
        };

        var result = Renderer.Render(Builder.Build(items, DigestOptions.Default), new DigestRecipient(hostile, "en"));

        var allowedTags = new HashSet<string>(StringComparer.Ordinal)
        {
            "html", "head", "meta", "title", "body", "table", "tr", "td", "h1", "h2", "p", "ul", "li", "strong", "span", "hr"
        };
        foreach (System.Text.RegularExpressions.Match tag in Regex.Matches(result.HtmlBody, @"</?([a-zA-Z0-9]+)"))
        {
            Assert.Contains(tag.Groups[1].Value, allowedTags);
        }

        Assert.DoesNotContain("<script", result.HtmlBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<img", result.HtmlBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<a ", result.HtmlBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<style", result.HtmlBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onerror=\"", result.HtmlBody, StringComparison.OrdinalIgnoreCase);

        // Hostile text appears only in encoded form, and every attribute is a fixed template one.
        Assert.DoesNotContain(hostile, result.HtmlBody, StringComparison.Ordinal);
        Assert.Contains(WebUtility.HtmlDecode(WebUtility.HtmlEncode(hostile)), WebUtility.HtmlDecode(result.HtmlBody), StringComparison.Ordinal);
    }

    [Fact]
    public void Render_TemplateItself_HasNoLinksImagesScriptsOrExternalResources()
    {
        var html = Render(new[] { Movie("Heat"), Episode("Show", 1, 1, "E"), Album("A", "B") }).HtmlBody;

        foreach (var forbidden in new[] { "<a ", "href", "src=", "<img", "<script", "<link", "<iframe", "<form", "@import", "url(", "http:", "https:", "javascript:", "@font-face" })
        {
            Assert.DoesNotContain(forbidden, html, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Render_LineBreaksInTitles_CannotForgeLinesOrHeaders()
    {
        var items = new[] { Movie("Real\r\nFake Heading\n---\r* forged") };

        var result = Renderer.Render(Builder.Build(items, DigestOptions.Default), new DigestRecipient("Ana\r\nBcc: x@evil.example", "en"));

        Assert.DoesNotContain("\r", result.TextBody, StringComparison.Ordinal);
        Assert.Contains("* Real Fake Heading --- * forged (2020)", result.TextBody, StringComparison.Ordinal);
        Assert.StartsWith("Hello Ana Bcc: x@evil.example,\n", result.TextBody, StringComparison.Ordinal);
        Assert.DoesNotContain('\r', result.Subject);
        Assert.DoesNotContain('\n', result.Subject);
    }

    [Fact]
    public void Render_BidiOverridesAndControls_AreRemoved()
    {
        var result = Render(new[] { Movie("Safe‮txt.exe\u0000\u0007") });

        Assert.Contains("* Safetxt.exe (2020)", result.TextBody, StringComparison.Ordinal);
        Assert.DoesNotContain("‮", result.HtmlBody, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_VeryLongTitle_IsTruncated()
    {
        var result = Render(new[] { Movie(new string('x', 5000)) });

        Assert.True(result.TextBody.Length < 1000);
        Assert.Contains("…", result.TextBody, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_DoesNotLeakIdentifiersOrPaths()
    {
        var item = Movie("Heat");
        item.LibraryId = Guid.NewGuid();
        var result = Render(new[] { item });

        foreach (var secret in new[] { item.ItemId.ToString(), item.ItemId.ToString("N"), item.LibraryId.ToString(), item.LibraryId.ToString("N") })
        {
            Assert.DoesNotContain(secret, result.HtmlBody, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(secret, result.TextBody, StringComparison.OrdinalIgnoreCase);
        }
    }

    // Parity

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("gl")]
    public void Render_HtmlAndTextCarryTheSameInformation(string language)
    {
        var result = RenderGolden(language);
        var htmlText = WebUtility.HtmlDecode(result.HtmlBody);

        // Every text node of the HTML body (title excluded) appears in the plain text.
        var body = result.HtmlBody[result.HtmlBody.IndexOf("<body", StringComparison.Ordinal)..];
        var nodes = Regex.Matches(body, @">([^<>]+)<")
            .Select(m => WebUtility.HtmlDecode(m.Groups[1].Value).Trim())
            .Where(n => n.Length > 0)
            .ToList();
        Assert.NotEmpty(nodes);
        Assert.All(nodes, node => Assert.Contains(node, result.TextBody, StringComparison.Ordinal));

        // And every piece of content in the plain text appears in the HTML.
        var pieces = result.TextBody
            .Split('\n')
            .Select(line => line.Trim().TrimStart('*', '-').Trim())
            .Where(line => line.Length > 0 && !line.All(c => c == '-'))
            .SelectMany(line => line.Split(" — "))
            .Select(piece => piece.Trim())
            .Where(piece => piece.Length > 0)
            .ToList();
        Assert.All(pieces, piece => Assert.Contains(piece, htmlText, StringComparison.Ordinal));
    }

    // Failures

    [Fact]
    public void Render_LocalizationFailure_IsWrappedAndNothingIsReturned()
    {
        var localization = new Mock<ILocalizationService>();
        localization.Setup(l => l.GetString(It.IsAny<string?>(), It.IsAny<string>())).Throws(new InvalidOperationException("boom"));
        var renderer = new DigestRenderer(localization.Object);

        var ex = Assert.Throws<DigestRenderException>(() => renderer.Render(Builder.Build(new[] { Movie("A") }, DigestOptions.Default), new DigestRecipient("Ana", "en")));

        Assert.IsType<InvalidOperationException>(ex.InnerException);
    }

    [Fact]
    public void Render_BadTranslationFormatString_IsWrapped()
    {
        var localization = new Mock<ILocalizationService>();
        localization.Setup(l => l.GetString(It.IsAny<string?>(), It.IsAny<string>())).Returns("{0} {1} {2}");
        var renderer = new DigestRenderer(localization.Object);

        Assert.Throws<DigestRenderException>(() => renderer.Render(Builder.Build(new[] { Movie("A") }, DigestOptions.Default), new DigestRecipient("Ana", "en")));
    }

    [Fact]
    public void Render_CategoryWithoutHeading_IsWrapped()
    {
        var model = new DigestModel(
            new[] { new DigestSection((MediaCategory)99, Array.Empty<DigestEntry>(), 1, 1, 1, 1) },
            new[] { Movie("A") });

        Assert.Throws<DigestRenderException>(() => Renderer.Render(model, new DigestRecipient("Ana", "en")));
    }

    [Fact]
    public void Render_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => Renderer.Render(null!, new DigestRecipient("Ana", "en")));
        Assert.Throws<ArgumentNullException>(() => Renderer.Render(Builder.Build(Array.Empty<DiscoveredMediaItem>(), DigestOptions.Default), null!));
    }

    [Fact]
    public void Render_DoesNotMutateTheModel()
    {
        var model = Builder.Build(GoldenItems(), DigestOptions.Default);
        var before = model.Items.Select(i => (i.ItemId, i.Name)).ToList();

        Renderer.Render(model, new DigestRecipient("Ana", "en"));

        Assert.Equal(before, model.Items.Select(i => (i.ItemId, i.Name)).ToList());
    }
}
