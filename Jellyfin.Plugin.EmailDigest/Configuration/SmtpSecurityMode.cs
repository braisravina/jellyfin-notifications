namespace Jellyfin.Plugin.EmailDigest.Configuration;

/// <summary>
/// The transport security mode used for the outgoing SMTP connection.
/// </summary>
public enum SmtpSecurityMode
{
    /// <summary>
    /// No transport encryption. Never chosen implicitly: an administrator must select it
    /// explicitly, it is refused together with authentication credentials, and the admin UI
    /// shows a security warning.
    /// </summary>
    None = 0,

    /// <summary>
    /// TLS negotiated after connecting in plain text (RFC 3207). Mandatory, not opportunistic:
    /// if the server does not offer or complete the upgrade, the connection fails rather than
    /// continuing in plain text.
    /// </summary>
    StartTls = 1,

    /// <summary>
    /// TLS from the first byte of the connection (commonly port 465).
    /// </summary>
    ImplicitTls = 2
}
