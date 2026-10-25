using System.Collections.Generic;
using System.Linq;
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
/// <param name="EpisodeListLimit">The most episodes a renderer lists under one series line.</param>
public sealed record DigestModel(IReadOnlyList<DigestSection> Sections, IReadOnlyList<DiscoveredMediaItem> Items, int EpisodeListLimit = 10)
{
    /// <summary>
    /// Gets a value indicating whether a display limit hid any entry. Hidden entries are not
    /// delivered by this digest and must stay pending for the next one.
    /// </summary>
    public bool HasHiddenEntries => Sections.Any(section => section.HiddenEntryCount > 0);

    /// <summary>
    /// Gets a value indicating whether there is nothing new to report.
    /// </summary>
    public bool IsEmpty => Items.Count == 0;
}
