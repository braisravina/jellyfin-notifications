namespace Jellyfin.Plugin.EmailDigest.Admin;

/// <summary>
/// A request to send one test message.
/// </summary>
/// <param name="Recipient">The destination address, explicitly chosen by the administrator.</param>
public sealed record TestEmailRequest(string? Recipient);
