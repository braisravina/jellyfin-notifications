using System;

namespace Jellyfin.Plugin.EmailDigest.Delivery;

/// <summary>
/// What a user may be told about their own deliveries. Purely informational: no decision about what
/// to send ever depends on it (that is <see cref="UserDeliveryState"/>), so losing it can only make
/// the page say "nothing yet".
/// </summary>
public sealed class UserDeliveryStatus
{
    /// <summary>
    /// Gets or sets the Jellyfin user id.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Gets or sets when the last attempt to send a digest was made.
    /// </summary>
    public DateTimeOffset? LastAttemptUtc { get; set; }

    /// <summary>
    /// Gets or sets how the last attempt ended.
    /// </summary>
    public DeliveryAttemptStatus? LastAttemptStatus { get; set; }

    /// <summary>
    /// Gets or sets when the SMTP server last accepted a digest for this user.
    /// </summary>
    public DateTimeOffset? LastAcceptedUtc { get; set; }
}
