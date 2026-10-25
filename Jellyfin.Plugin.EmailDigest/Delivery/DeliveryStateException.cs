using System;

namespace Jellyfin.Plugin.EmailDigest.Delivery;

/// <summary>
/// Thrown when the delivery state file exists but cannot be trusted (unreadable, damaged or written
/// by a newer version). The file is left exactly as it is.
/// </summary>
/// <remarks>
/// Treating such a file as "no state" would either resend old media or silently skip new media, so
/// a run must stop instead. Recovery is manual: restore <c>delivery-state.json.bak</c> or remove the
/// file after reading the documentation.
/// </remarks>
public sealed class DeliveryStateException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DeliveryStateException"/> class.
    /// </summary>
    /// <param name="message">What is wrong, without file contents.</param>
    /// <param name="innerException">The underlying failure, if any.</param>
    public DeliveryStateException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
