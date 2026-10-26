using System;

namespace Jellyfin.Plugin.EmailDigest.Verification;

/// <summary>
/// Thrown when verification state cannot be read or written. Verification then fails closed:
/// nothing is confirmed.
/// </summary>
public sealed class VerificationStoreException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="VerificationStoreException"/> class.
    /// </summary>
    /// <param name="message">What failed; never a token or an address.</param>
    /// <param name="innerException">The underlying error, if any.</param>
    public VerificationStoreException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
