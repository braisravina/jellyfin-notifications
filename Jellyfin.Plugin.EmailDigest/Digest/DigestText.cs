using System;
using System.Globalization;
using Jellyfin.Plugin.EmailDigest.Configuration;
using Jellyfin.Plugin.EmailDigest.Localization;
using Jellyfin.Plugin.EmailDigest.Services;

namespace Jellyfin.Plugin.EmailDigest.Digest;

/// <summary>
/// Localized wording for one recipient's digest. The single place that turns keys, counts and
/// categories into text, so the HTML and plain-text outputs cannot word things differently.
/// </summary>
/// <remarks>
/// An unsupported or missing language falls back to <see cref="SupportedLanguages.Default"/>
/// (English); a key missing from a language falls back to English (see
/// <see cref="ILocalizationService"/>). Plurals use "One" for exactly 1 and "Other" for everything
/// else, which is correct for English, Spanish and Galician; a language with other plural rules
/// would need a richer selector. Numbers are written with invariant digits.
/// </remarks>
public sealed class DigestText
{
    private readonly ILocalizationService _localization;

    /// <summary>
    /// Initializes a new instance of the <see cref="DigestText"/> class.
    /// </summary>
    /// <param name="localization">The localization service.</param>
    /// <param name="languageCode">The recipient's language, possibly unsupported or null.</param>
    public DigestText(ILocalizationService localization, string? languageCode)
    {
        ArgumentNullException.ThrowIfNull(localization);
        _localization = localization;
        Language = SupportedLanguages.IsSupported(languageCode)
            ? languageCode!.ToLowerInvariant()
            : SupportedLanguages.Default;
    }

    /// <summary>
    /// Gets the language actually used, after fallback.
    /// </summary>
    public string Language { get; }

    /// <summary>
    /// Gets a string with format arguments applied.
    /// </summary>
    /// <param name="key">The resource key.</param>
    /// <param name="args">The format arguments.</param>
    /// <returns>The localized, formatted text.</returns>
    public string Format(string key, params object[] args)
    {
        var template = _localization.GetString(Language, key);
        return args.Length == 0 ? template : string.Format(CultureInfo.InvariantCulture, template, args);
    }

    /// <summary>
    /// Gets the plural form of a message for a count; the resource keys are
    /// <c>{baseKey}One</c> and <c>{baseKey}Other</c> and receive the count as <c>{0}</c>.
    /// </summary>
    /// <param name="baseKey">The key without the plural suffix.</param>
    /// <param name="count">The count.</param>
    /// <returns>The localized text.</returns>
    public string Plural(string baseKey, int count)
    {
        return Format(baseKey + (count == 1 ? "One" : "Other"), count);
    }

    /// <summary>
    /// Gets the section heading for a category.
    /// </summary>
    /// <param name="category">The category.</param>
    /// <returns>The localized heading.</returns>
    public string Heading(MediaCategory category)
    {
        var key = category switch
        {
            MediaCategory.Movies => "SectionMovies",
            MediaCategory.Series => "SectionSeries",
            MediaCategory.Anime => "SectionAnime",
            MediaCategory.Documentaries => "SectionDocumentaries",
            MediaCategory.Music => "SectionMusic",
            MediaCategory.Other => "SectionOther",
            _ => throw new ArgumentOutOfRangeException(nameof(category), category, "No heading for this category.")
        };

        return Format(key);
    }
}
