using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.EmailDigest.Email;

/// <summary>
/// Hands messages to an SMTP server. Knows nothing about media, digests or checkpoints.
/// </summary>
public interface IEmailTransport
{
    /// <summary>
    /// Connects, secures the connection and logs in, then disconnects. Sends no message.
    /// </summary>
    /// <param name="settings">The server settings.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The outcome; never throws for network or protocol failures.</returns>
    Task<EmailTransportResult> TestConnectionAsync(SmtpSettings settings, CancellationToken cancellationToken);

    /// <summary>
    /// Sends one message to its one recipient over its own connection.
    /// </summary>
    /// <param name="settings">The server settings.</param>
    /// <param name="email">The message.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>
    /// The outcome; never throws for network or protocol failures. <c>Accepted</c> means the server
    /// took responsibility for the message, not that it reached the mailbox.
    /// </returns>
    Task<EmailTransportResult> SendAsync(SmtpSettings settings, OutgoingEmail email, CancellationToken cancellationToken);
}
