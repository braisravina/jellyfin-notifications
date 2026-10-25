using System;

namespace Jellyfin.Plugin.EmailDigest.Digest;

/// <summary>
/// Thrown when a digest could not be rendered.
/// </summary>
/// <remarks>
/// A caller must treat this as "nothing was delivered" and must not advance the checkpoint.
/// </remarks>
public sealed class DigestRenderException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DigestRenderException"/> class.
    /// </summary>
    /// <param name="message">What failed.</param>
    /// <param name="innerException">The underlying failure.</param>
    public DigestRenderException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
