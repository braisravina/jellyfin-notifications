using System;

namespace Jellyfin.Plugin.EmailDigest.Subscription;

/// <summary>
/// Makes and reads the tokens in unsubscribe links.
/// </summary>
public interface IUnsubscribeTokens
{
    /// <summary>
    /// Makes the unsubscribe token for a user. The same user always gets the same token, so links in
    /// older emails keep working.
    /// </summary>
    /// <param name="userId">The Jellyfin user.</param>
    /// <returns>A URL-safe token.</returns>
    string Create(Guid userId);

    /// <summary>
    /// Reads a token.
    /// </summary>
    /// <param name="token">The token from a link.</param>
    /// <returns>The user it was made for, or null when it is not a token this server made.</returns>
    Guid? TryRead(string? token);
}
