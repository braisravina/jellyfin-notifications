using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.EmailDigest.Configuration;
using Jellyfin.Plugin.EmailDigest.Email;
using Jellyfin.Plugin.EmailDigest.Localization;
using Jellyfin.Plugin.EmailDigest.Services;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.EmailDigest.Admin;

/// <summary>
/// The logic behind the administrator endpoints: show and change SMTP settings, manage the
/// password, test the connection and send a test message.
/// </summary>
/// <remarks>
/// Nothing here returns a password or a raw exception. Responses are built from the
/// <see cref="Admin"/> DTOs and localized, fixed message keys only. Authorization is the
/// controller's job (<c>RequiresElevation</c>); this class assumes its caller is an administrator.
/// </remarks>
public sealed class SmtpAdminService
{
    private static readonly TimeSpan TestEmailInterval = TimeSpan.FromSeconds(5);

    private readonly ISmtpConfigurationAccessor _configuration;
    private readonly ISmtpPasswordStore _passwords;
    private readonly IEmailTransport _transport;
    private readonly ILocalizationService _localization;
    private readonly TimeProvider _time;
    private readonly ILogger<SmtpAdminService> _logger;
    private readonly object _throttleLock = new();
    private DateTimeOffset _lastTestEmail = DateTimeOffset.MinValue;

    /// <summary>
    /// Initializes a new instance of the <see cref="SmtpAdminService"/> class.
    /// </summary>
    /// <param name="configuration">Configuration access.</param>
    /// <param name="passwords">The SMTP password store.</param>
    /// <param name="transport">The SMTP transport.</param>
    /// <param name="localization">The localization service.</param>
    /// <param name="time">The clock.</param>
    /// <param name="logger">The logger.</param>
    public SmtpAdminService(
        ISmtpConfigurationAccessor configuration,
        ISmtpPasswordStore passwords,
        IEmailTransport transport,
        ILocalizationService localization,
        TimeProvider time,
        ILogger<SmtpAdminService> logger)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(passwords);
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(localization);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(logger);
        _configuration = configuration;
        _passwords = passwords;
        _transport = transport;
        _localization = localization;
        _time = time;
        _logger = logger;
    }

    /// <summary>
    /// Gets the current SMTP settings.
    /// </summary>
    /// <returns>200 with <see cref="SmtpSettingsDto"/>.</returns>
    public AdminResponse GetSettings()
    {
        return new AdminResponse(200, ToDto(_configuration.GetCopy()));
    }

    /// <summary>
    /// Changes the SMTP settings, leaving everything else (and the password) untouched.
    /// </summary>
    /// <param name="update">The new settings.</param>
    /// <returns>200 with the saved settings, or 400 with the validation errors.</returns>
    public AdminResponse UpdateSettings(SmtpSettingsUpdate? update)
    {
        if (update is null)
        {
            return Error(400, "request");
        }

        if (!Enum.TryParse<SmtpSecurityMode>(update.Security, ignoreCase: true, out var security) || !Enum.IsDefined(security))
        {
            return new AdminResponse(400, new { errors = new[] { "Security must be None, StartTls or ImplicitTls." } });
        }

        var configuration = _configuration.GetCopy();
        var before = _configuration.GetCopy();
        var hadStoredPassword = _passwords.GetStatus().Source == SmtpPasswordSource.Stored;
        configuration.SmtpEnabled = update.SmtpEnabled;
        configuration.SmtpHost = (update.Host ?? string.Empty).Trim();
        configuration.SmtpPort = update.Port;
        configuration.SmtpSecurity = security;
        configuration.SmtpUsername = (update.Username ?? string.Empty).Trim();
        configuration.SenderEmailAddress = (update.SenderAddress ?? string.Empty).Trim();
        configuration.SenderDisplayName = (update.SenderName ?? string.Empty).Trim();
        configuration.SmtpTimeoutSeconds = update.TimeoutSeconds;

        var errors = PluginConfigurationValidator.Validate(configuration);
        if (errors.Count > 0)
        {
            return new AdminResponse(400, new { errors });
        }

        try
        {
            _configuration.Save(configuration);
        }
        catch (PluginConfigurationValidationException ex)
        {
            return new AdminResponse(400, new { errors = ex.Errors });
        }

        _logger.LogInformation("SMTP settings were updated by an administrator.");

        // Plugin.UpdateConfiguration removes the stored password when the server it was entered
        // for changes; report it so the page can ask for the password again.
        var cleared = hadStoredPassword
                      && ConfigurationChangePolicy.MustClearStoredPassword(before, configuration)
                      && _passwords.GetStatus().Source != SmtpPasswordSource.Stored;
        return new AdminResponse(200, ToDto(_configuration.GetCopy()) with { PasswordCleared = cleared });
    }

    /// <summary>
    /// Replaces the stored password.
    /// </summary>
    /// <param name="update">The new password.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>200 with the password status (never the password), 400 if invalid, 409 if managed externally.</returns>
    public async Task<AdminResponse> SetPasswordAsync(SmtpPasswordUpdate? update, CancellationToken cancellationToken)
    {
        if (update?.Password is null)
        {
            return Error(400, "request");
        }

        try
        {
            await _passwords.SetAsync(update.Password, cancellationToken).ConfigureAwait(false);
        }
        catch (SmtpPasswordException ex)
        {
            return PasswordFailure(ex);
        }

        _logger.LogInformation("The SMTP password was replaced by an administrator.");
        return new AdminResponse(200, ToDto(_passwords.GetStatus()));
    }

    /// <summary>
    /// Removes the stored password.
    /// </summary>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>200 with the password status, or 409 if managed externally.</returns>
    public async Task<AdminResponse> ClearPasswordAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _passwords.ClearAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (SmtpPasswordException ex)
        {
            return PasswordFailure(ex);
        }

        _logger.LogInformation("The SMTP password was cleared by an administrator.");
        return new AdminResponse(200, ToDto(_passwords.GetStatus()));
    }

    /// <summary>
    /// Tests the saved settings: connects, secures and logs in, but sends no message.
    /// </summary>
    /// <param name="language">The administrator's language for the explanation.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>200 with the outcome.</returns>
    public async Task<AdminResponse> TestConnectionAsync(string? language, CancellationToken cancellationToken)
    {
        var settings = await BuildSettingsAsync(cancellationToken).ConfigureAwait(false);
        var result = await _transport.TestConnectionAsync(settings, cancellationToken).ConfigureAwait(false);
        return new AdminResponse(200, ToDto(result, language));
    }

    /// <summary>
    /// Sends one test message to an explicitly chosen address.
    /// </summary>
    /// <param name="request">The destination.</param>
    /// <param name="language">The language of the test message and explanation.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>200 with the outcome, 400 for an invalid address, 429 if called again too soon.</returns>
    public async Task<AdminResponse> SendTestEmailAsync(TestEmailRequest? request, string? language, CancellationToken cancellationToken)
    {
        var recipient = request?.Recipient?.Trim();
        if (!ConfigurationValidation.IsValidEmail(recipient) || !ConfigurationValidation.IsValidSingleLine(recipient, 254))
        {
            return Error(400, "recipient");
        }

        lock (_throttleLock)
        {
            var now = _time.GetUtcNow();
            if (now - _lastTestEmail < TestEmailInterval)
            {
                return Error(429, "throttled");
            }

            _lastTestEmail = now;
        }

        var text = new Digest.DigestText(_localization, language);
        var body = text.Format("TestEmailBody");
        var html = "<!DOCTYPE html><html lang=\"" + text.Language + "\"><body><p>" + WebUtility.HtmlEncode(body) + "</p></body></html>";
        var email = new OutgoingEmail(recipient!, null, text.Format("TestEmailSubject"), body, html);

        var settings = await BuildSettingsAsync(cancellationToken).ConfigureAwait(false);
        var result = await _transport.SendAsync(settings, email, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("An administrator sent a test email: {Outcome}.", result.Outcome);
        return new AdminResponse(200, ToDto(result, language));
    }

    /// <summary>
    /// Gets the administrator-page strings for a language.
    /// </summary>
    /// <param name="language">The requested language; unsupported values give English.</param>
    /// <returns>200 with a key-to-text map.</returns>
    public AdminResponse GetStrings(string? language)
    {
        var text = new Digest.DigestText(_localization, language);
        var strings = AdminStringKeys.All.ToDictionary(key => key, key => text.Format(key), StringComparer.Ordinal);
        return new AdminResponse(200, new { language = text.Language, strings });
    }

    private static string CategoryOf(EmailTransportOutcome outcome)
    {
        return outcome switch
        {
            EmailTransportOutcome.Accepted => "ok",
            EmailTransportOutcome.InvalidRequest => "configuration",
            EmailTransportOutcome.ConnectionFailed or EmailTransportOutcome.Timeout or EmailTransportOutcome.Cancelled => "connectivity",
            EmailTransportOutcome.TlsFailed => "tls",
            EmailTransportOutcome.Uncertain => "other",
            EmailTransportOutcome.AuthenticationFailed => "authentication",
            EmailTransportOutcome.RecipientRejected or EmailTransportOutcome.SenderRejected => "recipient",
            _ => "other"
        };
    }

    private static SmtpPasswordStatusDto ToDto(SmtpPasswordStatus status)
    {
        return new SmtpPasswordStatusDto(status.IsSet, status.Source.ToString());
    }

    private AdminResponse PasswordFailure(SmtpPasswordException exception)
    {
        return exception.Failure == SmtpPasswordFailure.ManagedExternally
            ? Error(409, "managedExternally")
            : Error(400, "invalidPassword");
    }

    private static AdminResponse Error(int status, string code)
    {
        return new AdminResponse(status, new { error = code });
    }

    private SmtpSettingsDto ToDto(PluginConfiguration configuration)
    {
        return new SmtpSettingsDto(
            configuration.SmtpEnabled,
            configuration.SmtpHost,
            configuration.SmtpPort,
            configuration.SmtpSecurity.ToString(),
            configuration.SmtpUsername,
            configuration.SenderEmailAddress,
            configuration.SenderDisplayName,
            configuration.SmtpTimeoutSeconds,
            ToDto(_passwords.GetStatus()));
    }

    private TransportResultDto ToDto(EmailTransportResult result, string? language)
    {
        var text = new Digest.DigestText(_localization, language);
        return new TransportResultDto(
            result.Outcome.ToString(),
            CategoryOf(result.Outcome),
            text.Format("AdminOutcome" + result.Outcome),
            result.SmtpStatusCode);
    }

    private async Task<SmtpSettings> BuildSettingsAsync(CancellationToken cancellationToken)
    {
        var password = await _passwords.ReadAsync(cancellationToken).ConfigureAwait(false);
        return SmtpSettings.From(_configuration.GetCopy(), password);
    }
}
