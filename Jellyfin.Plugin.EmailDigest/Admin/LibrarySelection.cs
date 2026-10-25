using System;

namespace Jellyfin.Plugin.EmailDigest.Admin;

/// <summary>
/// One monitored library chosen by the administrator.
/// </summary>
/// <param name="LibraryId">The library id.</param>
/// <param name="Category">The digest category: Movies, Series, Anime, Documentaries, Music or Other.</param>
public sealed record LibrarySelection(Guid LibraryId, string? Category);
