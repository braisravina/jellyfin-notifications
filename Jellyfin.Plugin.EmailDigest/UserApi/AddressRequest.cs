namespace Jellyfin.Plugin.EmailDigest.UserApi;

/// <summary>
/// A request to use a new notification address.
/// </summary>
/// <param name="Address">The address to verify.</param>
public sealed record AddressRequest(string? Address);
