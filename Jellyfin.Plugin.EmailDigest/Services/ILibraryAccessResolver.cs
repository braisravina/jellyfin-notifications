using System;
using Jellyfin.Database.Implementations.Entities;

namespace Jellyfin.Plugin.EmailDigest.Services;

/// <summary>
/// Decides, using Jellyfin's own rules, whether a configured library may be queried for a user.
/// </summary>
/// <remarks>
/// Needed because <c>ILibraryManager.GetItemList</c> only intersects a query with the user's
/// permitted libraries when the query carries no explicit <c>TopParentIds</c>; once the plugin
/// narrows to the monitored libraries itself, that check no longer runs and has to be done here.
/// </remarks>
public interface ILibraryAccessResolver
{
    /// <summary>
    /// Resolves a monitored library for a user.
    /// </summary>
    /// <param name="user">The Jellyfin user the digest is being built for.</param>
    /// <param name="libraryId">The configured library (virtual folder) id.</param>
    /// <returns>The access decision and, when accessible, the ids to query by.</returns>
    LibraryAccess Resolve(User user, Guid libraryId);
}
