using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.EmailDigest.Configuration;
using Jellyfin.Plugin.EmailDigest.Digest;
using Jellyfin.Plugin.EmailDigest.Localization;
using Jellyfin.Plugin.EmailDigest.Services;
using Xunit;

namespace Jellyfin.Plugin.EmailDigest.Tests.Digest;

public sealed class DigestTextTests
{
    private static readonly string[] Languages = { "en", "es", "gl" };

    private static Dictionary<string, string> LoadResource(string language)
    {
        var assembly = typeof(LocalizationService).Assembly;
        var name = typeof(SupportedLanguages).Namespace + "." + language + ".json";
        using var stream = assembly.GetManifestResourceStream(name);
        Assert.NotNull(stream);
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream!)!;
    }

    private static DigestText Text(string? language)
    {
        return new DigestText(new LocalizationService(), language);
    }

    [Fact]
    public void Resources_EveryLanguageHasExactlyTheEnglishKeys()
    {
        var english = LoadResource("en").Keys.OrderBy(k => k, StringComparer.Ordinal).ToList();

        foreach (var language in Languages.Where(l => l != "en"))
        {
            Assert.Equal(english, LoadResource(language).Keys.OrderBy(k => k, StringComparer.Ordinal).ToList());
        }
    }

    [Fact]
    public void Resources_NoValueIsBlank()
    {
        foreach (var language in Languages)
        {
            Assert.All(LoadResource(language), pair => Assert.False(string.IsNullOrWhiteSpace(pair.Value), language + ":" + pair.Key));
        }
    }

    [Fact]
    public void Resources_PlaceholdersMatchEnglishInEveryLanguage()
    {
        var english = LoadResource("en");

        foreach (var language in Languages.Where(l => l != "en"))
        {
            var translated = LoadResource(language);
            foreach (var (key, value) in english)
            {
                Assert.Equal(Placeholders(value), Placeholders(translated[key]));
            }
        }
    }

    [Fact]
    public void Resources_EveryPluralKeyHasBothForms()
    {
        foreach (var language in Languages)
        {
            var keys = LoadResource(language).Keys;
            foreach (var one in keys.Where(k => k.EndsWith("One", StringComparison.Ordinal)))
            {
                Assert.Contains(one[..^3] + "Other", keys);
            }
        }
    }

    [Fact]
    public void Heading_EveryCategoryHasAHeadingInEveryLanguage()
    {
        foreach (var language in Languages)
        {
            var text = Text(language);
            foreach (var category in Enum.GetValues<MediaCategory>())
            {
                var heading = text.Heading(category);
                Assert.False(string.IsNullOrWhiteSpace(heading));
                Assert.DoesNotContain("Section", heading, StringComparison.Ordinal);
            }
        }
    }

    [Theory]
    [InlineData("en", MediaCategory.Documentaries, "Documentaries")]
    [InlineData("es", MediaCategory.Documentaries, "Documentales")]
    [InlineData("gl", MediaCategory.Documentaries, "Documentais")]
    [InlineData("es", MediaCategory.Music, "Música")]
    [InlineData("gl", MediaCategory.Other, "Outros")]
    public void Heading_UsesTheRecipientsLanguage(string language, MediaCategory category, string expected)
    {
        Assert.Equal(expected, Text(language).Heading(category));
    }

    [Theory]
    [InlineData("en", 1, "1 new episode")]
    [InlineData("en", 0, "0 new episodes")]
    [InlineData("en", 2, "2 new episodes")]
    [InlineData("es", 1, "1 episodio nuevo")]
    [InlineData("es", 5, "5 episodios nuevos")]
    [InlineData("gl", 1, "1 episodio novo")]
    [InlineData("gl", 5, "5 episodios novos")]
    public void Plural_ChoosesSingularOnlyForOne(string language, int count, string expected)
    {
        Assert.Equal(expected, Text(language).Plural("EpisodeCount", count));
    }

    [Theory]
    [InlineData("en", 5, "and 5 more")]
    [InlineData("es", 5, "y 5 más")]
    [InlineData("gl", 5, "e 5 máis")]
    public void Plural_OverflowWording(string language, int count, string expected)
    {
        Assert.Equal(expected, Text(language).Plural("Overflow", count));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("fr")]
    [InlineData("zz-ZZ")]
    public void Language_UnsupportedOrMissing_FallsBackToEnglish(string? requested)
    {
        var text = Text(requested);

        Assert.Equal("en", text.Language);
        Assert.Equal("Movies", text.Heading(MediaCategory.Movies));
    }

    [Fact]
    public void Language_IsCaseInsensitiveAndNormalised()
    {
        Assert.Equal("es", Text("ES").Language);
    }

    [Fact]
    public void Format_AppliesArgumentsWithInvariantDigits()
    {
        Assert.Equal("Hola Ana,", Text("es").Format("Greeting", "Ana"));
        Assert.Equal("S01E02", Text("en").Format("SeasonEpisode", "01", "02"));
    }

    [Fact]
    public void Format_UnknownKey_ReturnsTheKeyInsteadOfThrowing()
    {
        Assert.Equal("NoSuchKey", Text("en").Format("NoSuchKey"));
    }

    private static string Placeholders(string value)
    {
        return string.Join(",", Regex.Matches(value, @"\{\d+\}").Select(m => m.Value).OrderBy(v => v, StringComparer.Ordinal));
    }
}
