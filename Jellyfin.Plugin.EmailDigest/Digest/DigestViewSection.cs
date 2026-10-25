using System.Collections.Generic;

namespace Jellyfin.Plugin.EmailDigest.Digest;

/// <summary>
/// One localized section.
/// </summary>
/// <param name="Heading">The section heading.</param>
/// <param name="Entries">The entries shown.</param>
/// <param name="Overflow">"and N more" for entries a limit hid, or null.</param>
internal sealed record DigestViewSection(string Heading, IReadOnlyList<DigestViewEntry> Entries, string? Overflow);
