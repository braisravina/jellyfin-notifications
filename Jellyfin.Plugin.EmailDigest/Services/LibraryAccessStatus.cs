namespace Jellyfin.Plugin.EmailDigest.Services;

/// <summary>
/// The outcome of resolving one monitored library for one Jellyfin user.
/// </summary>
public enum LibraryAccessStatus
{
    /// <summary>
    /// The library exists, is enabled, and the user is allowed to see it.
    /// </summary>
    Accessible = 0,

    /// <summary>
    /// No Jellyfin item has this id (for example the library was deleted).
    /// </summary>
    NotFound = 1,

    /// <summary>
    /// An item with this id exists but is not a top-level library.
    /// </summary>
    NotLibrary = 2,

    /// <summary>
    /// The library exists but is disabled, or this user is not allowed to see it.
    /// </summary>
    NotAccessible = 3
}
