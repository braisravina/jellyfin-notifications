namespace Jellyfin.Plugin.EmailDigest.Digest;

/// <summary>
/// A labelled footer link; the address has already been checked to be an absolute http(s) address.
/// </summary>
/// <param name="Label">The localized label.</param>
/// <param name="Url">The address.</param>
internal sealed record DigestViewLink(string Label, string Url);
