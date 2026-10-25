using System.Collections.Generic;

namespace Jellyfin.Plugin.EmailDigest.Digest;

/// <summary>
/// One localized, sanitized line group, ready for either output format.
/// </summary>
/// <param name="Title">The headline text.</param>
/// <param name="Detail">Secondary text on the same line, or null.</param>
/// <param name="SubLines">Indented lines under the headline (episodes).</param>
/// <param name="More">A closing "and N more" line for the sub-lines, or null.</param>
internal sealed record DigestViewEntry(string Title, string? Detail, IReadOnlyList<string> SubLines, string? More);
