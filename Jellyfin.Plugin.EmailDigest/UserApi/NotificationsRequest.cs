namespace Jellyfin.Plugin.EmailDigest.UserApi;

/// <summary>
/// A request to turn the signed-in user's digests on or off.
/// </summary>
/// <param name="Enabled">The wish; required.</param>
public sealed record NotificationsRequest(bool? Enabled);
