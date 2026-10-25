using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.EmailDigest.Services;

/// <summary>
/// Holds the SMTP password, outside the generic plugin configuration.
/// </summary>
/// <remarks>
/// The configuration object is returned to the browser by Jellyfin's generic configuration
/// endpoint, so it cannot hold a secret. This store is the only place the password lives. The
/// administrator API can learn only <see cref="GetStatus"/>; <see cref="ReadAsync"/> exists for
/// the SMTP transport and must never be exposed over HTTP.
/// </remarks>
public interface ISmtpPasswordStore
{
    /// <summary>
    /// Reports whether a password exists and where it comes from.
    /// </summary>
    /// <returns>The status; never the password.</returns>
    SmtpPasswordStatus GetStatus();

    /// <summary>
    /// Reads the password for use by the SMTP transport.
    /// </summary>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The password, or null when none is configured.</returns>
    Task<string?> ReadAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Replaces the stored password.
    /// </summary>
    /// <param name="password">The new password.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A task representing the operation.</returns>
    /// <exception cref="SmtpPasswordException">The value is invalid, or the password is managed externally.</exception>
    Task SetAsync(string password, CancellationToken cancellationToken);

    /// <summary>
    /// Removes the stored password.
    /// </summary>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A task representing the operation.</returns>
    /// <exception cref="SmtpPasswordException">The password is managed externally.</exception>
    Task ClearAsync(CancellationToken cancellationToken);
}
