using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.EmailDigest.Models;

namespace Jellyfin.Plugin.EmailDigest.Services;

/// <summary>
/// Persists per-user Email Digest preferences, keyed by stable Jellyfin user id.
/// </summary>
public interface IUserPreferenceRepository
{
    /// <summary>
    /// Gets one user's preferences.
    /// </summary>
    /// <param name="userId">The Jellyfin user id.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The user's preferences, or <see langword="null"/> if none are recorded yet.</returns>
    Task<UserNotificationPreferences?> GetAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Gets every recorded user's preferences.
    /// </summary>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>All recorded preferences.</returns>
    Task<IReadOnlyList<UserNotificationPreferences>> GetAllAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Creates or replaces one user's preferences.
    /// </summary>
    /// <param name="preferences">The preferences to save.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="preferences"/> has an invalid email address or unsupported language code.
    /// </exception>
    /// <returns>A task representing the asynchronous save operation.</returns>
    Task SaveAsync(UserNotificationPreferences preferences, CancellationToken cancellationToken);
}
