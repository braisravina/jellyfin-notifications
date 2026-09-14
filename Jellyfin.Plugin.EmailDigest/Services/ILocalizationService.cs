namespace Jellyfin.Plugin.EmailDigest.Services;

/// <summary>
/// Resolves localized display strings for digest emails.
/// </summary>
public interface ILocalizationService
{
    /// <summary>
    /// Gets the localized string for the given key and language.
    /// </summary>
    /// <param name="languageCode">
    /// The requested ISO 639-1 language code. Falls back to
    /// <see cref="Localization.SupportedLanguages.Default"/> if unsupported or unspecified.
    /// </param>
    /// <param name="key">The resource key.</param>
    /// <returns>
    /// The localized string, falling back to the English string if the key is missing from the
    /// requested language, or to the key itself if the key exists in no language at all.
    /// </returns>
    string GetString(string? languageCode, string key);
}
