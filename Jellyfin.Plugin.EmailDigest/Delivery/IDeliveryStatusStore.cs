using System;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.EmailDigest.Delivery;

/// <summary>
/// Keeps each user's last-attempt information, separately from delivery state.
/// </summary>
/// <remarks>
/// Separate on purpose: a failed attempt has no checkpoint to record, and writing a half-empty
/// <see cref="UserDeliveryState"/> would corrupt a new user's baseline. A failure to write here is
/// logged and ignored by callers; it must never affect what is sent.
/// </remarks>
public interface IDeliveryStatusStore
{
    /// <summary>
    /// Gets one user's status.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The status, or null when nothing has been attempted.</returns>
    Task<UserDeliveryStatus?> GetAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Records an attempt for one user, leaving every other user untouched. An accepted attempt also
    /// sets the last accepted time; a failed one keeps the previous one.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <param name="status">How the attempt ended.</param>
    /// <param name="atUtc">When.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A task representing the save.</returns>
    Task RecordAttemptAsync(Guid userId, DeliveryAttemptStatus status, DateTimeOffset atUtc, CancellationToken cancellationToken);
}
