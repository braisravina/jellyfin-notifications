using System;

namespace Jellyfin.Plugin.EmailDigest.Delivery;

/// <summary>
/// Looks up Jellyfin users for a digest run.
/// </summary>
public interface IUserDirectory
{
    /// <summary>
    /// Finds a user by their stable Jellyfin id.
    /// </summary>
    /// <param name="userId">The user id.</param>
    /// <returns>The user, or null when Jellyfin has no such user (for example it was deleted).</returns>
    UserInfo? Find(Guid userId);
}
