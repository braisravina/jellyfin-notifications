using System.Collections.Generic;
using Jellyfin.Plugin.EmailDigest.Configuration;

namespace Jellyfin.Plugin.EmailDigest.Digest;

/// <summary>
/// All new media of one category.
/// </summary>
/// <param name="Category">The category.</param>
/// <param name="Entries">The entries shown, in display order.</param>
/// <param name="TotalEntryCount">The number of entries that exist, shown or not.</param>
/// <param name="TotalItemCount">The number of underlying items (every episode counts).</param>
/// <param name="HiddenEntryCount">Entries cut by a limit.</param>
/// <param name="HiddenItemCount">Underlying items belonging to the hidden entries.</param>
public sealed record DigestSection(
    MediaCategory Category,
    IReadOnlyList<DigestEntry> Entries,
    int TotalEntryCount,
    int TotalItemCount,
    int HiddenEntryCount,
    int HiddenItemCount);
