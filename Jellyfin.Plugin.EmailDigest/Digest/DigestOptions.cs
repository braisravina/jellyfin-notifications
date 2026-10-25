using System;

namespace Jellyfin.Plugin.EmailDigest.Digest;

/// <summary>
/// Display limits applied when building a digest. They only decide what is shown; every eligible
/// item stays in <see cref="DigestModel.Items"/>.
/// </summary>
public sealed class DigestOptions
{
    /// <summary>
    /// Gets the default options.
    /// </summary>
    public static DigestOptions Default { get; } = new();

    /// <summary>
    /// Gets the most entries (a movie, an album or a whole series) shown in one section.
    /// </summary>
    public int MaxEntriesPerSection { get; init; } = 25;

    /// <summary>
    /// Gets the most entries shown in the whole digest. Sections are filled in their fixed
    /// order, so when this runs out the later sections show only an overflow count.
    /// </summary>
    public int MaxEntriesPerDigest { get; init; } = 100;

    /// <summary>
    /// Throws when a limit is not positive.
    /// </summary>
    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxEntriesPerSection, 1, nameof(MaxEntriesPerSection));
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxEntriesPerDigest, 1, nameof(MaxEntriesPerDigest));
    }
}
