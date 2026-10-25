namespace Jellyfin.Plugin.EmailDigest.Email;

/// <summary>
/// What happened when the plugin tried to hand a message (or a connection test) to an SMTP server.
/// </summary>
public enum EmailTransportOutcome
{
    /// <summary>
    /// The SMTP server accepted the message (or the connection and login worked). This is NOT
    /// proof of delivery: the server may still bounce it later or the mailbox may never be read.
    /// </summary>
    Accepted = 0,

    /// <summary>
    /// The request was invalid (bad settings, bad recipient, bad message), so nothing was sent.
    /// </summary>
    InvalidRequest = 1,

    /// <summary>
    /// The server could not be reached.
    /// </summary>
    ConnectionFailed = 2,

    /// <summary>
    /// A secure connection could not be established: the server does not support the requested
    /// encryption, the handshake failed, or its certificate is not trusted. The connection is
    /// never continued in plain text.
    /// </summary>
    TlsFailed = 3,

    /// <summary>
    /// The server refused the username or password.
    /// </summary>
    AuthenticationFailed = 4,

    /// <summary>
    /// The server refused the sender address.
    /// </summary>
    SenderRejected = 5,

    /// <summary>
    /// The server refused the recipient address.
    /// </summary>
    RecipientRejected = 6,

    /// <summary>
    /// The operation took longer than the configured timeout.
    /// </summary>
    Timeout = 7,

    /// <summary>
    /// The caller cancelled the operation.
    /// </summary>
    Cancelled = 8,

    /// <summary>
    /// Anything else, including SMTP errors not covered above.
    /// </summary>
    UnexpectedError = 9
}
