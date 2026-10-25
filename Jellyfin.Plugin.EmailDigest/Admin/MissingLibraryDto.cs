using System;

namespace Jellyfin.Plugin.EmailDigest.Admin;

/// <summary>
/// A configured library that Jellyfin no longer has.
/// </summary>
/// <param name="Id">The library id.</param>
/// <param name="Category">The category it was configured with.</param>
public sealed record MissingLibraryDto(Guid Id, string Category);
