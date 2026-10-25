using System;

namespace Jellyfin.Plugin.EmailDigest.Delivery;

/// <summary>
/// An item that was included in a message the SMTP server accepted for a user.
/// </summary>
/// <remarks>
/// Remembered for a while (<see cref="UserDeliveryState.DeliveredRetention"/>) so media that Jellyfin
/// re-creates under the same id with a new creation date is not announced twice, and so items shown
/// in a digest whose checkpoint could only advance partway are not repeated.
/// </remarks>
public sealed class DeliveredItem
{
    /// <summary>
    /// Gets or sets the Jellyfin item id.
    /// </summary>
    public Guid ItemId { get; set; }

    /// <summary>
    /// Gets or sets when the message containing it was accepted.
    /// </summary>
    public DateTimeOffset DeliveredUtc { get; set; }
}
