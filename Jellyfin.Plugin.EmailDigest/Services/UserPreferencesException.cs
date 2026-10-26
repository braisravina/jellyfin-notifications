using System;

namespace Jellyfin.Plugin.EmailDigest.Services;

/// <summary>
/// Thrown when the stored preferences cannot be trusted: the file is damaged, has an unsupported
/// format or contains an invalid record. The file is left exactly as found.
/// </summary>
public sealed class UserPreferencesException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UserPreferencesException"/> class.
    /// </summary>
    /// <param name="message">What is wrong; never file content.</param>
    /// <param name="innerException">The underlying error, if any.</param>
    public UserPreferencesException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
