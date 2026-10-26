namespace Jellyfin.Plugin.EmailDigest.Delivery;

/// <summary>
/// The coarse result of the last attempt to send a user a digest. Deliberately says nothing about
/// why, so no error text, server name or address can leak through it.
/// </summary>
public enum DeliveryAttemptStatus
{
    /// <summary>The SMTP server accepted the message. That is not proof it reached the inbox.</summary>
    Accepted,

    /// <summary>Sending did not succeed; it will be tried again at the next run.</summary>
    Failed,

    /// <summary>The server may or may not have accepted the message.</summary>
    Uncertain
}
