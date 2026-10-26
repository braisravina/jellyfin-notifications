namespace Jellyfin.Plugin.EmailDigest.Digest;

/// <summary>
/// The only recipient details a digest uses.
/// </summary>
/// <param name="DisplayName">The name to greet; null or blank gives a nameless greeting.</param>
/// <param name="Language">The recipient's preferred language; unsupported values fall back to English.</param>
/// <param name="UnsubscribeUrl">The absolute link that turns this recipient's digests off, shown in the footer; ignored unless http(s).</param>
/// <param name="PreferencesUrl">The absolute link to the recipient's preferences page, shown in the footer; ignored unless http(s).</param>
public sealed record DigestRecipient(string? DisplayName, string? Language, string? UnsubscribeUrl = null, string? PreferencesUrl = null);
