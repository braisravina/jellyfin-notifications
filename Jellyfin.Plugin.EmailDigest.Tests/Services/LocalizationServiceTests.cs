using Jellyfin.Plugin.EmailDigest.Services;
using Xunit;

namespace Jellyfin.Plugin.EmailDigest.Tests.Services;

public class LocalizationServiceTests
{
    private readonly LocalizationService _service = new();

    [Theory]
    [InlineData("en", "SectionMovies", "Movies")]
    [InlineData("es", "SectionMovies", "Películas")]
    [InlineData("gl", "SectionMovies", "Películas")]
    [InlineData("en", "Season", "Season")]
    [InlineData("es", "Season", "Temporada")]
    [InlineData("gl", "Season", "Tempada")]
    [InlineData("en", "NewEpisodes", "new episode(s)")]
    [InlineData("es", "NewEpisodes", "episodio(s) nuevo(s)")]
    [InlineData("gl", "NewEpisodes", "episodio(s) novo(s)")]
    public void GetString_SupportedLanguageAndKey_ReturnsTranslatedValue(string language, string key, string expected)
    {
        var result = _service.GetString(language, key);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("SectionSeries")]
    [InlineData("SectionAnime")]
    [InlineData("SectionMusic")]
    [InlineData("EmailSubject")]
    [InlineData("Greeting")]
    [InlineData("Intro")]
    [InlineData("Footer")]
    public void GetString_EveryKey_IsPresentInAllThreeLanguages(string key)
    {
        foreach (var language in new[] { "en", "es", "gl" })
        {
            var result = _service.GetString(language, key);
            Assert.NotEqual(key, result); // falling back to the raw key means the translation is missing
        }
    }

    [Fact]
    public void GetString_UnsupportedLanguage_FallsBackToEnglish()
    {
        var english = _service.GetString("en", "SectionMovies");
        var unsupported = _service.GetString("fr", "SectionMovies");

        Assert.Equal(english, unsupported);
    }

    [Fact]
    public void GetString_NullLanguage_FallsBackToEnglish()
    {
        var english = _service.GetString("en", "SectionMovies");
        var nullLanguage = _service.GetString(null, "SectionMovies");

        Assert.Equal(english, nullLanguage);
    }

    [Fact]
    public void GetString_KeyMissingEverywhere_ReturnsTheKeyItselfRatherThanThrowing()
    {
        var result = _service.GetString("en", "NoSuchKeyExistsAnywhere");

        Assert.Equal("NoSuchKeyExistsAnywhere", result);
    }
}
