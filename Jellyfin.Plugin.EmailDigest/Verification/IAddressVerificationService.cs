using System;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.EmailDigest.Verification;

/// <summary>
/// Issues and redeems the one-time links that prove a user can read mail sent to an address.
/// </summary>
public interface IAddressVerificationService
{
    /// <summary>
    /// Starts verifying an address for a user, replacing any earlier outstanding request of theirs.
    /// </summary>
    /// <param name="userId">The user, taken from the authenticated session, never from the browser.</param>
    /// <param name="address">The address to verify.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The token to email, or why none was issued.</returns>
    Task<IssueResult> IssueAsync(Guid userId, string? address, CancellationToken cancellationToken);

    /// <summary>
    /// Redeems a token: if it is valid it is used up first (and that is persisted), then
    /// <paramref name="apply"/> records the address. Single use therefore holds even if recording
    /// fails, and two simultaneous redemptions cannot both succeed.
    /// </summary>
    /// <param name="token">The token from the link.</param>
    /// <param name="apply">Records the confirmed address; returns false if it could not.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The outcome.</returns>
    Task<ConfirmOutcome> ConfirmAsync(string? token, Func<ConfirmedAddress, CancellationToken, Task<bool>> apply, CancellationToken cancellationToken);

    /// <summary>
    /// Gets a user's outstanding request, if any and still valid.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The request, or null.</returns>
    Task<PendingVerification?> GetPendingAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Cancels a user's outstanding request; its link stops working.
    /// </summary>
    /// <param name="userId">The user.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>True when there was a request to cancel.</returns>
    Task<bool> CancelAsync(Guid userId, CancellationToken cancellationToken);
}
