using System;

namespace Jellyfin.Plugin.EmailDigest.Storage;

/// <summary>
/// Thrown when state could not be brought to the stable data folder. The plugin then refuses to
/// run rather than start from empty state (which would resend media or lose subscriptions).
/// </summary>
public sealed class StorageMigrationException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="StorageMigrationException"/> class.
    /// </summary>
    /// <param name="message">What failed; never a file's content.</param>
    /// <param name="innerException">The underlying error, if any.</param>
    public StorageMigrationException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
