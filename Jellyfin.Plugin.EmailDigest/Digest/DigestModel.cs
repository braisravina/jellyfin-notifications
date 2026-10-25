using System.Collections.Generic;
using Jellyfin.Plugin.EmailDigest.Models;

namespace Jellyfin.Plugin.EmailDigest.Digest;

/// <summary>
/// A digest for one recipient, independent of HTML, text and SMTP.
/// </summary>
/// <param name="Sections">The non-empty sections in their fixed display order.</param>
/// <param name="Items">
/// Every eligible item, de-duplicated and ordered, whether or not a display limit hid it. Delivery
/// accounting must use this list, never the displayed entries.
/// </param>
public sealed record DigestModel(IReadOnlyList<DigestSection> Sections, IReadOnlyList<DiscoveredMediaItem> Items)
{
    /// <summary>
    /// Gets a value indicating whether there is nothing new to report.
    /// </summary>
    public bool IsEmpty => Items.Count == 0;
}
