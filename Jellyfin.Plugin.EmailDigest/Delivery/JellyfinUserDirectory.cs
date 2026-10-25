using System;
using Jellyfin.Data;
using Jellyfin.Database.Implementations.Enums;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.EmailDigest.Delivery;

/// <summary>
/// <see cref="IUserDirectory"/> backed by Jellyfin's user manager.
/// </summary>
public sealed class JellyfinUserDirectory : IUserDirectory
{
    private readonly IUserManager _userManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="JellyfinUserDirectory"/> class.
    /// </summary>
    /// <param name="userManager">Jellyfin's user manager.</param>
    public JellyfinUserDirectory(IUserManager userManager)
    {
        ArgumentNullException.ThrowIfNull(userManager);
        _userManager = userManager;
    }

    /// <inheritdoc />
    public UserInfo? Find(Guid userId)
    {
        var user = _userManager.GetUserById(userId);
        return user is null ? null : new UserInfo(user.Username, user.HasPermission(PermissionKind.IsDisabled));
    }
}
