using System;
using System.Linq;
using Jellyfin.Database.Implementations.Entities;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.EmailDigest.Services;

/// <summary>
/// Production <see cref="ILibraryAccessResolver"/> backed by Jellyfin's own library objects.
/// </summary>
/// <remarks>
/// Visibility is delegated to <see cref="CollectionFolder.IsVisible(User, bool)"/>, the same check
/// Jellyfin uses when it lists a user's libraries: it covers the library's own Enabled option, the
/// user's enabled/blocked folder lists, and parental-rating rules. No permission logic is
/// duplicated here. Covered by live verification rather than unit tests, since
/// <see cref="CollectionFolder"/> depends on Jellyfin's static service locator.
/// </remarks>
public sealed class JellyfinLibraryAccessResolver : ILibraryAccessResolver
{
    private readonly ILibraryManager _libraryManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="JellyfinLibraryAccessResolver"/> class.
    /// </summary>
    /// <param name="libraryManager">Jellyfin's library manager.</param>
    public JellyfinLibraryAccessResolver(ILibraryManager libraryManager)
    {
        ArgumentNullException.ThrowIfNull(libraryManager);
        _libraryManager = libraryManager;
    }

    /// <inheritdoc />
    public LibraryAccess Resolve(User user, Guid libraryId)
    {
        ArgumentNullException.ThrowIfNull(user);

        var item = libraryId == Guid.Empty ? null : _libraryManager.GetItemById(libraryId);
        if (item is null)
        {
            return new LibraryAccess(LibraryAccessStatus.NotFound, Array.Empty<Guid>());
        }

        if (item is not CollectionFolder library)
        {
            return new LibraryAccess(LibraryAccessStatus.NotLibrary, Array.Empty<Guid>());
        }

        // A library with no physical folders has nothing to query; treating it as inaccessible
        // keeps an empty TopParentIds from ever reaching a query (which would mean "everything").
        var topParentIds = library.PhysicalFolderIds.Where(id => id != Guid.Empty).Distinct().ToArray();
        if (!library.IsVisible(user) || topParentIds.Length == 0)
        {
            return new LibraryAccess(LibraryAccessStatus.NotAccessible, Array.Empty<Guid>());
        }

        return new LibraryAccess(LibraryAccessStatus.Accessible, topParentIds);
    }
}
