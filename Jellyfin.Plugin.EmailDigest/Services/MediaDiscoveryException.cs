using System;

namespace Jellyfin.Plugin.EmailDigest.Services;

/// <summary>
/// Thrown when media discovery could not complete because Jellyfin failed.
/// </summary>
/// <remarks>
/// Distinct from an empty result on purpose: callers must not advance the digest checkpoint
/// after a failure, or the items that were never examined would be skipped forever.
/// </remarks>
public sealed class MediaDiscoveryException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="MediaDiscoveryException"/> class.
    /// </summary>
    /// <param name="message">What failed.</param>
    /// <param name="innerException">The underlying Jellyfin failure.</param>
    public MediaDiscoveryException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
