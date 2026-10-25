using System;

namespace Jellyfin.Plugin.EmailDigest.Admin;

/// <summary>
/// One Jellyfin library as shown on the administrator page.
/// </summary>
/// <param name="Id">The stable library id.</param>
/// <param name="Name">The library's current display name (untrusted text; renamed libraries keep their id).</param>
/// <param name="CollectionType">Jellyfin's collection type, for example "movies"; may be empty.</param>
/// <param name="SuggestedCategory">A starting suggestion for the category dropdown only; empty when none is safe to suggest.</param>
/// <param name="Monitored">Whether the library is currently monitored.</param>
/// <param name="Category">The configured category, or empty when not monitored.</param>
public sealed record LibraryDto(Guid Id, string Name, string CollectionType, string SuggestedCategory, bool Monitored, string Category);
