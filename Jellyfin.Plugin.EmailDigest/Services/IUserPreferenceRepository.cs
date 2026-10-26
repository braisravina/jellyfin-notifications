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
    /// <exception cref="UserPreferencesException">The stored file cannot be trusted.</exception>
    Task<UserNotificationPreferences?> GetAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Gets every recorded user's preferences.
    /// </summary>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>All recorded preferences.</returns>
    /// <exception cref="UserPreferencesException">The stored file cannot be trusted.</exception>
    Task<IReadOnlyList<UserNotificationPreferences>> GetAllAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Creates or replaces one user's whole record, validating it first.
    /// </summary>
    /// <param name="preferences">The preferences to save.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="preferences"/> has an invalid email address or unsupported language code.
    /// </exception>
    /// <exception cref="UserPreferencesException">The stored file cannot be trusted; nothing is overwritten.</exception>
    /// <returns>A task representing the asynchronous save operation.</returns>
    Task SaveAsync(UserNotificationPreferences preferences, CancellationToken cancellationToken);

    /// <summary>
    /// Atomically reads one user's record, changes it, validates the result and stores it, with no
    /// other change to the file able to interleave. Changes to other users are never touched.
    /// </summary>
    /// <param name="userId">The Jellyfin user id; the only record that can change.</param>
    /// <param name="change">
    /// Receives a copy of the current record (or <see langword="null"/> when there is none) and returns
    /// the new record, or <see langword="null"/> to remove it. It runs under the repository lock, so it
    /// must be quick and must not call the repository.
    /// </param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The stored record, or <see langword="null"/> when it was removed.</returns>
    /// <exception cref="ArgumentException">The result names another user, or has an invalid address or language.</exception>
    /// <exception cref="UserPreferencesException">The stored file cannot be trusted; nothing is overwritten.</exception>
    Task<UserNotificationPreferences?> UpdateAsync(
        Guid userId,
        Func<UserNotificationPreferences?, UserNotificationPreferences?> change,
        CancellationToken cancellationToken);
}
