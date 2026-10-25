using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.EmailDigest.Delivery;

/// <summary>
/// Persists per-user delivery state in the plugin's data folder.
/// </summary>
public interface IDeliveryStateStore
{
    /// <summary>
    /// Gets one user's state.
    /// </summary>
    /// <param name="userId">The Jellyfin user id.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The state, or null when the user has none yet.</returns>
    /// <exception cref="DeliveryStateException">The state file cannot be trusted.</exception>
    Task<UserDeliveryState?> GetAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Gets every user's state.
    /// </summary>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>All records.</returns>
    /// <exception cref="DeliveryStateException">The state file cannot be trusted.</exception>
    Task<IReadOnlyList<UserDeliveryState>> GetAllAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Atomically creates or replaces one user's state, leaving every other user's untouched.
    /// </summary>
    /// <param name="state">The state to save.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A task representing the save.</returns>
    /// <exception cref="DeliveryStateException">The existing state file cannot be trusted; nothing is overwritten.</exception>
    /// <exception cref="System.IO.IOException">The write failed; the previous state is intact.</exception>
    Task SaveAsync(UserDeliveryState state, CancellationToken cancellationToken);

    /// <summary>
    /// Reads the global checkpoint written by versions before per-user state existed.
    /// </summary>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The legacy checkpoint, or null when there is none or it is unreadable.</returns>
    /// <remarks>The legacy file is only ever read: it is never modified, moved or deleted.</remarks>
    Task<DateTimeOffset?> GetLegacyCheckpointAsync(CancellationToken cancellationToken);
}
