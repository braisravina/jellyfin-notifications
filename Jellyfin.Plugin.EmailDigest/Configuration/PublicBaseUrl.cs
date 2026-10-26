using System;

namespace Jellyfin.Plugin.EmailDigest.Configuration;

/// <summary>
/// Validates and builds the absolute links placed in emails.
/// </summary>
/// <remarks>
/// The base comes only from administrator configuration. A link must never be derived from a request
/// (its <c>Host</c> or <c>X-Forwarded-*</c> headers are caller-controlled), or anyone able to trigger an
/// email could make it carry a link to a site of their choosing.
/// </remarks>
public static class PublicBaseUrl
{
    /// <summary>
    /// The path of the page that confirms an address.
    /// </summary>
    public const string VerifyPath = "EmailDigest/Verify";

    /// <summary>
    /// The path of the page and endpoint that unsubscribe a recipient.
    /// </summary>
    public const string UnsubscribePath = "EmailDigest/Unsubscribe";

    /// <summary>
    /// The path of the signed-in user's preferences page.
    /// </summary>
    public const string PreferencesPath = "EmailDigest/Me/Page";

    /// <summary>
    /// Normalizes a configured base address.
    /// </summary>
    /// <param name="value">The configured value.</param>
    /// <returns>The address without a trailing slash, or null when it is not acceptable.</returns>
    public static string? TryNormalize(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed) || trimmed.Length > 200 || !ConfigurationValidation.IsValidSingleLine(trimmed, 200))
        {
            return null;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
            || string.IsNullOrEmpty(uri.Host)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)
            || trimmed.Contains('\\', StringComparison.Ordinal)
            || trimmed.Contains(' ', StringComparison.Ordinal))
        {
            return null;
        }

        return trimmed.TrimEnd('/');
    }

    /// <summary>
    /// Builds a link under the base address.
    /// </summary>
    /// <param name="baseUrl">The configured base address.</param>
    /// <param name="path">One of the path constants.</param>
    /// <param name="token">An optional URL-safe token placed in the query.</param>
    /// <returns>The absolute link, or null when the base is not acceptable.</returns>
    public static string? Link(string? baseUrl, string path, string? token = null)
    {
        var normalized = TryNormalize(baseUrl);
        if (normalized is null)
        {
            return null;
        }

        return token is null ? normalized + "/" + path : normalized + "/" + path + "?token=" + Uri.EscapeDataString(token);
    }
}
