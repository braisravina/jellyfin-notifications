namespace Jellyfin.Plugin.EmailDigest.Digest;

/// <summary>
/// The only recipient details a digest uses.
/// </summary>
/// <param name="DisplayName">The name to greet; null or blank gives a nameless greeting.</param>
/// <param name="Language">The recipient's preferred language; unsupported values fall back to English.</param>
public sealed record DigestRecipient(string? DisplayName, string? Language);
