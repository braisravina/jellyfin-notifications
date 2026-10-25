using System;

namespace Jellyfin.Plugin.EmailDigest.Delivery;

/// <summary>
/// The span of time one delivery run examines for one user: items created after
/// <see cref="FromUtc"/> (exclusive) up to and including <see cref="ToUtc"/>.
/// </summary>
/// <param name="FromUtc">The exclusive start.</param>
/// <param name="ToUtc">The inclusive end, fixed for the whole run.</param>
/// <param name="Source">Where the start came from.</param>
public readonly record struct DeliveryWindow(DateTimeOffset FromUtc, DateTimeOffset ToUtc, DeliveryWindowSource Source)
{
    /// <summary>
    /// Gets a value indicating whether there is nothing to examine.
    /// </summary>
    public bool IsEmpty => ToUtc <= FromUtc;
}
