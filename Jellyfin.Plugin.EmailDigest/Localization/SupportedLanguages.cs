using System.Collections.Generic;

namespace Jellyfin.Plugin.EmailDigest.Localization;

/// <summary>
/// The single source of truth for which language codes this plugin supports.
/// </summary>
/// <remarks>
/// Shared by configuration validation and the plugin's localization service so the two
/// never drift apart.
/// </remarks>
public static class SupportedLanguages
{
    /// <summary>
    /// The language code used when a requested language is unsupported or unspecified.
    /// </summary>
    public const string Default = "en";

    /// <summary>
    /// Gets the set of supported ISO 639-1 language codes.
    /// </summary>
    public static IReadOnlySet<string> Codes { get; } = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
    {
        "en",
        "es",
        "gl"
    };

    /// <summary>
    /// Determines whether the given language code is supported.
    /// </summary>
    /// <param name="languageCode">The language code to check.</param>
    /// <returns><see langword="true"/> if supported; otherwise <see langword="false"/>.</returns>
    public static bool IsSupported(string? languageCode)
    {
        return languageCode is not null && Codes.Contains(languageCode);
    }
}
