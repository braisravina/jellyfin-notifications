namespace Jellyfin.Plugin.EmailDigest.Delivery;

/// <summary>
/// How the runner reacts to a transport outcome.
/// </summary>
public enum DeliveryFailureKind
{
    /// <summary>
    /// The server accepted the message.
    /// </summary>
    Accepted = 0,

    /// <summary>
    /// A temporary problem: try again a bounded number of times.
    /// </summary>
    Transient = 1,

    /// <summary>
    /// A problem with this user's message or address: do not retry; other users are unaffected.
    /// </summary>
    PermanentForUser = 2,

    /// <summary>
    /// The SMTP configuration cannot work (login, TLS, sender): stop the run, because every other
    /// user would fail the same way and retrying risks locking the account.
    /// </summary>
    StopRun = 3,

    /// <summary>
    /// Whether the server accepted the message is unknown: not retried now, not marked delivered.
    /// </summary>
    Uncertain = 4,

    /// <summary>
    /// The caller cancelled.
    /// </summary>
    Cancelled = 5
}
