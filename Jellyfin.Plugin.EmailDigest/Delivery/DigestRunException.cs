using System;

namespace Jellyfin.Plugin.EmailDigest.Delivery;

/// <summary>
/// Thrown by the scheduled task when a run ended with failures or was aborted, so Jellyfin's
/// Scheduled Tasks page shows the task as failed instead of successful. The message carries counts
/// and a reason code only: no names, addresses or titles.
/// </summary>
public sealed class DigestRunException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DigestRunException"/> class.
    /// </summary>
    /// <param name="message">The summary of what went wrong.</param>
    public DigestRunException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DigestRunException"/> class.
    /// </summary>
    public DigestRunException()
        : base("The Email Digest run did not complete successfully.")
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DigestRunException"/> class.
    /// </summary>
    /// <param name="message">The summary of what went wrong.</param>
    /// <param name="innerException">The underlying failure.</param>
    public DigestRunException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
