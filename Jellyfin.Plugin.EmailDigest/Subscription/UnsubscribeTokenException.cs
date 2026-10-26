using System;

namespace Jellyfin.Plugin.EmailDigest.Subscription;

/// <summary>
/// Thrown when the unsubscribe key cannot be used. Nothing is sent without a working key, because a
/// digest must carry a working unsubscribe link.
/// </summary>
public sealed class UnsubscribeTokenException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UnsubscribeTokenException"/> class.
    /// </summary>
    /// <param name="message">What is wrong; never the key.</param>
    /// <param name="innerException">The underlying error, if any.</param>
    public UnsubscribeTokenException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
