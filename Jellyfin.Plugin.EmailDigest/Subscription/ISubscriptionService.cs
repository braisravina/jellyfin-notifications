using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.EmailDigest.Verification;

namespace Jellyfin.Plugin.EmailDigest.Subscription;

/// <summary>
/// What a user can do to their own email notifications, and what links in emails can do.
/// </summary>
/// <remarks>
/// Every method that takes a user id expects it from the authenticated session. Nothing here accepts a
/// user id from a browser, and nothing can touch another user's preferences or delivery state.
/// </remarks>
public interface ISubscriptionService
{
    /// <summary>
    /// Gets the user's own overview.
    /// </summary>
    /// <param name="userId">The authenticated user.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The overview, or null when the user does not exist or is disabled.</returns>
    Task<SubscriptionOverview?> GetOverviewAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Turns the user's digests on or off.
    /// </summary>
    /// <param name="userId">The authenticated user.</param>
    /// <param name="enabled">The wish.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The result.</returns>
    Task<SubscriptionChange> SetNotificationsAsync(Guid userId, bool enabled, CancellationToken cancellationToken);

    /// <summary>
    /// Sets the language of the user's digests and emails.
    /// </summary>
    /// <param name="userId">The authenticated user.</param>
    /// <param name="language">en, es or gl.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The result.</returns>
    Task<SubscriptionChange> SetLanguageAsync(Guid userId, string? language, CancellationToken cancellationToken);

    /// <summary>
    /// Asks to use a new address: emails a one-time link to it. The address in use does not change
    /// until the link is opened.
    /// </summary>
    /// <param name="userId">The authenticated user.</param>
    /// <param name="address">The address to verify.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The result.</returns>
    Task<AddressRequestResult> RequestAddressAsync(Guid userId, string? address, CancellationToken cancellationToken);

    /// <summary>
    /// Withdraws the user's outstanding address request.
    /// </summary>
    /// <param name="userId">The authenticated user.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>True when there was one.</returns>
    Task<bool> CancelPendingAddressAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Redeems a verification link's token. The link is the proof; no sign-in is needed, because the
    /// recipient may open the email on another device.
    /// </summary>
    /// <param name="token">The token from the link.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The outcome.</returns>
    Task<ConfirmOutcome> ConfirmAddressAsync(string? token, CancellationToken cancellationToken);

    /// <summary>
    /// Turns off the notifications of the user an unsubscribe token was made for. It does nothing else:
    /// it does not delete the account, the address, the language or any delivery history, and it never
    /// returns any of them.
    /// </summary>
    /// <param name="token">The token from an unsubscribe link.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>True when the token was valid and notifications are now off; false for an invalid token or a storage error.</returns>
    Task<bool> UnsubscribeAsync(string? token, CancellationToken cancellationToken);
}
