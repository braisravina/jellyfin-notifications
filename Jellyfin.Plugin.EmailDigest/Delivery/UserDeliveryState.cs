using System;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.EmailDigest.Delivery;

/// <summary>
/// What the plugin knows about delivery to one Jellyfin user. Every user has an independent record,
/// so one user's failure, pause or new subscription never affects another's.
/// </summary>
public sealed class UserDeliveryState
{
    /// <summary>
    /// How long delivered item ids are remembered.
    /// </summary>
    public static readonly TimeSpan DeliveredRetention = TimeSpan.FromDays(90);

    /// <summary>
    /// Gets or sets the Jellyfin user id.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Gets or sets the exclusive start of the next discovery window: items created after this
    /// instant, up to the next run's upper bound, are still to be examined.
    /// </summary>
    public DateTimeOffset CheckpointUtc { get; set; }

    /// <summary>
    /// Gets or sets when this record was last written.
    /// </summary>
    public DateTimeOffset UpdatedUtc { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the last delivery attempt, for diagnosis. It identifies one
    /// attempt, not a message the mail provider is known to deduplicate.
    /// </summary>
    public string? LastAttemptId { get; set; }

    /// <summary>
    /// Gets or sets a short outcome label of the last attempt, for diagnosis.
    /// </summary>
    public string? LastOutcome { get; set; }

    /// <summary>
    /// Gets items delivered recently. Changed in place; loaded by populating this collection.
    /// </summary>
    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public Collection<DeliveredItem> Delivered { get; } = new();

    /// <summary>
    /// Drops delivered items older than the retention period.
    /// </summary>
    /// <param name="now">The reference time.</param>
    public void PruneDelivered(DateTimeOffset now)
    {
        var cutoff = now - DeliveredRetention;
        for (var i = Delivered.Count - 1; i >= 0; i--)
        {
            if (Delivered[i].DeliveredUtc <= cutoff)
            {
                Delivered.RemoveAt(i);
            }
        }
    }
}
