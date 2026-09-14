namespace Jellyfin.Plugin.EmailDigest.Configuration;

/// <summary>
/// The transport security mode used for the outgoing SMTP connection.
/// </summary>
public enum SmtpSecurityMode
{
    /// <summary>
    /// No transport encryption.
    /// </summary>
    None = 0,

    /// <summary>
    /// Opportunistic TLS negotiated after connecting in plain text (RFC 3207).
    /// </summary>
    StartTls = 1,

    /// <summary>
    /// TLS from the first byte of the connection (commonly port 465).
    /// </summary>
    ImplicitTls = 2
}
