using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.Json;
using Jellyfin.Plugin.EmailDigest.Localization;

namespace Jellyfin.Plugin.EmailDigest.Services;

/// <inheritdoc />
public sealed class LocalizationService : ILocalizationService
{
    private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> _resourcesByLanguage;

    /// <summary>
    /// Initializes a new instance of the <see cref="LocalizationService"/> class, eagerly loading
    /// every embedded language resource so lookups never touch disk after construction.
    /// </summary>
    public LocalizationService()
    {
        var assembly = typeof(LocalizationService).Assembly;
        var resources = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var languageCode in SupportedLanguages.Codes)
        {
            resources[languageCode] = LoadResource(assembly, languageCode);
        }

        _resourcesByLanguage = resources;
    }

    /// <inheritdoc />
    public string GetString(string? languageCode, string key)
    {
        var effectiveLanguage = SupportedLanguages.IsSupported(languageCode)
            ? languageCode!
            : SupportedLanguages.Default;

        if (_resourcesByLanguage.TryGetValue(effectiveLanguage, out var strings)
            && strings.TryGetValue(key, out var value))
        {
            return value;
        }

        // Fall back to English for a key missing from a non-English resource.
        if (!string.Equals(effectiveLanguage, SupportedLanguages.Default, StringComparison.OrdinalIgnoreCase)
            && _resourcesByLanguage.TryGetValue(SupportedLanguages.Default, out var englishStrings)
            && englishStrings.TryGetValue(key, out var englishValue))
        {
            return englishValue;
        }

        // The key exists in no language resource at all: never throw, return something visible.
        return key;
    }

    private static IReadOnlyDictionary<string, string> LoadResource(Assembly assembly, string languageCode)
    {
        // SupportedLanguages lives in the Localization namespace, next to the embedded *.json files.
        var resourceName = string.Format(
            CultureInfo.InvariantCulture,
            "{0}.{1}.json",
            typeof(SupportedLanguages).Namespace,
            languageCode);

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            return new Dictionary<string, string>();
        }

        var result = JsonSerializer.Deserialize<Dictionary<string, string>>(stream);
        return result ?? new Dictionary<string, string>();
    }
}
