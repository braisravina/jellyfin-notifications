namespace Jellyfin.Plugin.EmailDigest.Subscription;

/// <summary>
/// What became of a request to use a new notification address.
/// </summary>
public enum AddressRequestStatus
{
    /// <summary>A confirmation email was accepted by the SMTP server.</summary>
    Sent,

    /// <summary>The SMTP server may or may not have accepted the email; the link stays valid in case it arrives.</summary>
    SentUncertain,

    /// <summary>The address is not a valid single mailbox.</summary>
    InvalidAddress,

    /// <summary>Verification is unavailable: SMTP is off or incomplete, or no public address is configured.</summary>
    NotAvailable,

    /// <summary>The address is already this user's verified address; nothing was sent.</summary>
    AlreadyVerified,

    /// <summary>Too many requests; see the retry time.</summary>
    RateLimited,

    /// <summary>The email could not be sent; the request was withdrawn.</summary>
    SendFailed,

    /// <summary>The user does not exist or is disabled.</summary>
    UserUnavailable,

    /// <summary>Verification state could not be read or written.</summary>
    StorageError
}
