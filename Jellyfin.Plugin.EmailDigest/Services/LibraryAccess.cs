using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.EmailDigest.Services;

/// <summary>
/// The result of <see cref="ILibraryAccessResolver.Resolve"/>.
/// </summary>
/// <param name="Status">Whether the library may be queried on this user's behalf.</param>
/// <param name="TopParentIds">
/// The physical folder ids Jellyfin stores as <c>TopParentId</c> on the library's items. Only
/// populated, and never empty, when <paramref name="Status"/> is
/// <see cref="LibraryAccessStatus.Accessible"/>.
/// </param>
public readonly record struct LibraryAccess(LibraryAccessStatus Status, IReadOnlyList<Guid> TopParentIds);
