namespace Jellyfin.Plugin.EmailDigest.Delivery;

/// <summary>
/// The Jellyfin facts about a user that a digest run needs.
/// </summary>
/// <param name="DisplayName">The user's Jellyfin user name, used to greet them.</param>
/// <param name="IsDisabled">Whether the Jellyfin account is disabled.</param>
public sealed record UserInfo(string DisplayName, bool IsDisabled);
