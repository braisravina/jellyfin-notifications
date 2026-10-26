using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.EmailDigest.Admin;
using Jellyfin.Plugin.EmailDigest.Configuration;
using Jellyfin.Plugin.EmailDigest.Delivery;
using Jellyfin.Plugin.EmailDigest.Email;
using Jellyfin.Plugin.EmailDigest.Localization;
using Jellyfin.Plugin.EmailDigest.Models;
using Jellyfin.Plugin.EmailDigest.Services;
using Jellyfin.Plugin.EmailDigest.Verification;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.EmailDigest.Subscription;

/// <inheritdoc />
/// <remarks>
/// <para>
/// Policy. The address digests go to changes only when a replacement has been confirmed from a link sent
/// to it; until then the current verified address stays in use and the pending one is never used. A
/// user who has no verified address is never sent a digest, whatever they enabled. Unsubscribing changes
/// exactly one flag. Logs carry no address, token or user name.
/// </para>
/// <para>
/// Authorization is by construction: every method acts only on the user id it is given, and that id comes
/// from the authenticated session.
/// </para>
/// </remarks>
public sealed class SubscriptionService : ISubscriptionService
{
    private readonly IUserPreferenceRepository _preferences;
    private readonly IAddressVerificationService _verification;
    private readonly IDeliveryStatusStore _status;
    private readonly IScheduleInfo _schedule;
    private readonly ISmtpConfigurationAccessor _configuration;
    private readonly ISmtpPasswordStore _passwords;
    private readonly IEmailTransport _transport;
    private readonly VerificationMessageBuilder _messages;
    private readonly IUnsubscribeTokens _unsubscribe;
    private readonly IUserDirectory _users;
    private readonly TimeProvider _time;
    private readonly ILogger<SubscriptionService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SubscriptionService"/> class.
    /// </summary>
    /// <param name="preferences">Per-user preferences.</param>
    /// <param name="verification">The verification link service.</param>
    /// <param name="status">Per-user delivery status.</param>
    /// <param name="schedule">The digest task's schedule.</param>
    /// <param name="configuration">Configuration access.</param>
    /// <param name="passwords">The SMTP password store.</param>
    /// <param name="transport">The SMTP transport.</param>
    /// <param name="messages">The verification message builder.</param>
    /// <param name="unsubscribe">The unsubscribe tokens.</param>
    /// <param name="users">Jellyfin user lookup.</param>
    /// <param name="time">The clock.</param>
    /// <param name="logger">The logger.</param>
    public SubscriptionService(
        IUserPreferenceRepository preferences,
        IAddressVerificationService verification,
        IDeliveryStatusStore status,
        IScheduleInfo schedule,
        ISmtpConfigurationAccessor configuration,
        ISmtpPasswordStore passwords,
        IEmailTransport transport,
        VerificationMessageBuilder messages,
        IUnsubscribeTokens unsubscribe,
        IUserDirectory users,
        TimeProvider time,
        ILogger<SubscriptionService> logger)
    {
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        _verification = verification ?? throw new ArgumentNullException(nameof(verification));
        _status = status ?? throw new ArgumentNullException(nameof(status));
        _schedule = schedule ?? throw new ArgumentNullException(nameof(schedule));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _passwords = passwords ?? throw new ArgumentNullException(nameof(passwords));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _messages = messages ?? throw new ArgumentNullException(nameof(messages));
        _unsubscribe = unsubscribe ?? throw new ArgumentNullException(nameof(unsubscribe));
        _users = users ?? throw new ArgumentNullException(nameof(users));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<SubscriptionOverview?> GetOverviewAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (!UserIsUsable(userId))
        {
            return null;
        }

        var config = _configuration.GetCopy();
        UserNotificationPreferences? preferences;
        UserDeliveryStatus? status;
        PendingVerification? pending;
        try
        {
            preferences = await _preferences.GetAsync(userId, cancellationToken).ConfigureAwait(false);
            status = await _status.GetAsync(userId, cancellationToken).ConfigureAwait(false);
            pending = await _verification.GetPendingAsync(userId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is UserPreferencesException or VerificationStoreException or System.IO.IOException)
        {
            _logger.LogWarning("A user's overview could not be read ({ExceptionType}).", ex.GetType().Name);
            return null;
        }

        var digestsActive = config.DigestEnabled && !config.DigestDryRun && config.SmtpEnabled;
        var language = preferences is not null && SupportedLanguages.IsSupported(preferences.Language) ? preferences.Language : config.DefaultLanguage;
        var verified = preferences?.EmailVerifiedUtc is not null && !string.IsNullOrEmpty(preferences.Email);

        return new SubscriptionOverview(
            preferences?.NotificationsEnabled ?? false,
            language,
            preferences?.Email ?? string.Empty,
            verified,
            preferences?.EmailVerifiedUtc,
            pending?.Address,
            pending?.ExpiresUtc,
            digestsActive && preferences is not null && preferences.CanReceiveDigest(),
            digestsActive,
            VerificationIsAvailable(config),
            status?.LastAcceptedUtc,
            status?.LastAttemptUtc,
            status?.LastAttemptStatus,
            config.DigestEnabled ? _schedule.GetNextRunUtc() : null);
    }

    /// <inheritdoc />
    public async Task<SubscriptionChange> SetNotificationsAsync(Guid userId, bool enabled, CancellationToken cancellationToken)
    {
        if (!UserIsUsable(userId))
        {
            return SubscriptionChange.UserUnavailable;
        }

        var defaultLanguage = _configuration.GetCopy().DefaultLanguage;
        return await ChangeAsync(
            userId,
            p =>
            {
                p ??= NewRecord(userId, defaultLanguage);
                p.NotificationsEnabled = enabled;
                return p;
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<SubscriptionChange> SetLanguageAsync(Guid userId, string? language, CancellationToken cancellationToken)
    {
        if (!SupportedLanguages.IsSupported(language))
        {
            return SubscriptionChange.InvalidValue;
        }

        if (!UserIsUsable(userId))
        {
            return SubscriptionChange.UserUnavailable;
        }

        var normalized = language!.ToLowerInvariant();
        return await ChangeAsync(
            userId,
            p =>
            {
                p ??= NewRecord(userId, normalized);
                p.Language = normalized;
                return p;
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<AddressRequestResult> RequestAddressAsync(Guid userId, string? address, CancellationToken cancellationToken)
    {
        if (!UserIsUsable(userId))
        {
            return new AddressRequestResult(AddressRequestStatus.UserUnavailable);
        }

        var normalized = AddressVerificationService.Normalize(address);
        if (normalized is null)
        {
            return new AddressRequestResult(AddressRequestStatus.InvalidAddress);
        }

        var config = _configuration.GetCopy();
        if (!VerificationIsAvailable(config))
        {
            return new AddressRequestResult(AddressRequestStatus.NotAvailable);
        }

        try
        {
            var current = await _preferences.GetAsync(userId, cancellationToken).ConfigureAwait(false);
            if (current?.EmailVerifiedUtc is not null && string.Equals(current.Email, normalized, StringComparison.OrdinalIgnoreCase))
            {
                return new AddressRequestResult(AddressRequestStatus.AlreadyVerified);
            }

            var issued = await _verification.IssueAsync(userId, normalized, cancellationToken).ConfigureAwait(false);
            if (issued.RateLimit is not null)
            {
                return new AddressRequestResult(AddressRequestStatus.RateLimited, null, issued.RateLimit, issued.RetryAfter);
            }

            if (!issued.Issued)
            {
                return new AddressRequestResult(AddressRequestStatus.InvalidAddress);
            }

            var language = current is not null && SupportedLanguages.IsSupported(current.Language) ? current.Language : config.DefaultLanguage;
            var link = PublicBaseUrl.Link(config.PublicBaseUrl, PublicBaseUrl.VerifyPath, issued.Token);
            var email = _messages.Build(normalized, language, config.SenderDisplayName, link!, issued.ExpiresUtc);
            var password = await _passwords.ReadAsync(cancellationToken).ConfigureAwait(false);
            var result = await _transport.SendAsync(SmtpSettings.From(config, password), email, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("An address verification email was requested: {Outcome}.", result.Outcome);

            if (result.IsAccepted)
            {
                return new AddressRequestResult(AddressRequestStatus.Sent, issued.ExpiresUtc);
            }

            if (result.Outcome == EmailTransportOutcome.Uncertain)
            {
                return new AddressRequestResult(AddressRequestStatus.SentUncertain, issued.ExpiresUtc);
            }

            // The link never reached anyone: withdraw it so no live token exists that nobody holds.
            await _verification.CancelAsync(userId, CancellationToken.None).ConfigureAwait(false);
            return new AddressRequestResult(AddressRequestStatus.SendFailed);
        }
        catch (Exception ex) when (ex is UserPreferencesException or VerificationStoreException or SmtpPasswordException or System.IO.IOException)
        {
            _logger.LogWarning("An address request failed ({ExceptionType}).", ex.GetType().Name);
            return new AddressRequestResult(AddressRequestStatus.StorageError);
        }
    }

    /// <inheritdoc />
    public async Task<bool> CancelPendingAddressAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (!UserIsUsable(userId))
        {
            return false;
        }

        try
        {
            return await _verification.CancelAsync(userId, cancellationToken).ConfigureAwait(false);
        }
        catch (VerificationStoreException)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<ConfirmOutcome> ConfirmAddressAsync(string? token, CancellationToken cancellationToken)
    {
        try
        {
            return await _verification.ConfirmAsync(token, ApplyConfirmedAddressAsync, cancellationToken).ConfigureAwait(false);
        }
        catch (VerificationStoreException)
        {
            return ConfirmOutcome.Invalid;
        }
    }

    /// <inheritdoc />
    public async Task<bool> UnsubscribeAsync(string? token, CancellationToken cancellationToken)
    {
        Guid? userId;
        try
        {
            userId = _unsubscribe.TryRead(token);
        }
        catch (UnsubscribeTokenException ex)
        {
            _logger.LogError("Unsubscribe is unavailable ({ExceptionType}).", ex.GetType().Name);
            return false;
        }

        if (userId is null)
        {
            return false;
        }

        try
        {
            var current = await _preferences.GetAsync(userId.Value, cancellationToken).ConfigureAwait(false);
            if (current is null || !current.NotificationsEnabled)
            {
                return true;
            }

            await _preferences.UpdateAsync(
                userId.Value,
                p =>
                {
                    if (p is not null)
                    {
                        p.NotificationsEnabled = false;
                    }

                    return p;
                },
                cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("A user unsubscribed from digests by link.");
            return true;
        }
        catch (UserPreferencesException)
        {
            _logger.LogWarning("An unsubscribe could not be saved: the preferences file cannot be trusted.");
            return false;
        }
    }

    private static UserNotificationPreferences NewRecord(Guid userId, string language)
    {
        return new UserNotificationPreferences { UserId = userId, Language = language };
    }

    private static bool VerificationIsAvailable(PluginConfiguration config)
    {
        return config.SmtpEnabled
               && PublicBaseUrl.TryNormalize(config.PublicBaseUrl) is not null
               && ConfigurationValidation.IsValidEmail(config.SenderEmailAddress);
    }

    private bool UserIsUsable(Guid userId)
    {
        if (userId == Guid.Empty)
        {
            return false;
        }

        var user = _users.Find(userId);
        return user is not null && !user.IsDisabled;
    }

    private async Task<SubscriptionChange> ChangeAsync(Guid userId, Func<UserNotificationPreferences?, UserNotificationPreferences?> change, CancellationToken cancellationToken)
    {
        try
        {
            await _preferences.UpdateAsync(userId, change, cancellationToken).ConfigureAwait(false);
            return SubscriptionChange.Saved;
        }
        catch (ArgumentException)
        {
            return SubscriptionChange.InvalidValue;
        }
        catch (UserPreferencesException ex)
        {
            _logger.LogWarning("A preference could not be saved ({ExceptionType}).", ex.GetType().Name);
            return SubscriptionChange.StorageError;
        }
    }

    private async Task<bool> ApplyConfirmedAddressAsync(ConfirmedAddress confirmed, CancellationToken cancellationToken)
    {
        // A user removed or disabled since asking cannot be given an address.
        if (!UserIsUsable(confirmed.UserId))
        {
            return false;
        }

        var now = _time.GetUtcNow();
        var defaultLanguage = _configuration.GetCopy().DefaultLanguage;
        try
        {
            await _preferences.UpdateAsync(
                confirmed.UserId,
                p =>
                {
                    p ??= NewRecord(confirmed.UserId, defaultLanguage);
                    p.Email = confirmed.Address;
                    p.EmailVerifiedUtc = now;
                    return p;
                },
                cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("A user's notification address was verified.");
            return true;
        }
        catch (Exception ex) when (ex is UserPreferencesException or ArgumentException)
        {
            _logger.LogWarning("A verified address could not be saved ({ExceptionType}).", ex.GetType().Name);
            return false;
        }
    }
}
