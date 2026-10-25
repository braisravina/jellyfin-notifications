using System;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.EmailDigest.Configuration;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.EmailDigest.Email;

/// <summary>
/// <see cref="IEmailTransport"/> built on MailKit. All SMTP protocol work is MailKit's.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>Encryption is never "auto": implicit TLS uses <c>SslOnConnect</c>, STARTTLS uses
/// <c>StartTls</c> (which fails if the server does not offer it, instead of continuing in plain
/// text), and unencrypted is used only when explicitly configured.</description></item>
/// <item><description>Server certificates are validated with the platform defaults, including
/// revocation checks. There is no setting and no code path in production to relax this.</description></item>
/// <item><description>Credentials are only ever sent over an encrypted connection.</description></item>
/// <item><description>Each message uses its own connection, so recipients never share a session.</description></item>
/// <item><description>Every failure becomes an <see cref="EmailTransportOutcome"/>. Exception messages,
/// server replies, addresses and credentials are never logged or returned.</description></item>
/// </list>
/// </remarks>
public sealed class MailKitEmailTransport : IEmailTransport
{
    // connect, login and send each get up to the configured timeout; this bounds the whole operation.
    private const int OperationBudgetFactor = 3;
    private const int MinTimeoutSeconds = 1;
    private const int MaxTimeoutSeconds = 300;

    private readonly ILogger<MailKitEmailTransport> _logger;
    private readonly RemoteCertificateValidationCallback? _testCertificateValidation;

    /// <summary>
    /// Initializes a new instance of the <see cref="MailKitEmailTransport"/> class.
    /// </summary>
    /// <param name="logger">The logger.</param>
    public MailKitEmailTransport(ILogger<MailKitEmailTransport> logger)
        : this(logger, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="MailKitEmailTransport"/> class with a
    /// certificate validation override. Reachable only from the test assembly.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="testCertificateValidation">Replaces certificate validation; tests only.</param>
    internal MailKitEmailTransport(ILogger<MailKitEmailTransport> logger, RemoteCertificateValidationCallback? testCertificateValidation)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
        _testCertificateValidation = testCertificateValidation;
    }

    private enum Phase
    {
        Connect,
        Authenticate,
        Send
    }

    /// <inheritdoc />
    public Task<EmailTransportResult> TestConnectionAsync(SmtpSettings settings, CancellationToken cancellationToken)
    {
        return RunAsync(settings, null, cancellationToken);
    }

    /// <inheritdoc />
    public Task<EmailTransportResult> SendAsync(SmtpSettings settings, OutgoingEmail email, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(email);
        return RunAsync(settings, email, cancellationToken);
    }

    private static SecureSocketOptions ToSocketOptions(SmtpSecurityMode mode)
    {
        return mode switch
        {
            SmtpSecurityMode.ImplicitTls => SecureSocketOptions.SslOnConnect,
            SmtpSecurityMode.StartTls => SecureSocketOptions.StartTls,
            SmtpSecurityMode.None => SecureSocketOptions.None,
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown security mode.")
        };
    }

    private static bool SettingsAreValid(SmtpSettings settings)
    {
        if (!ConfigurationValidation.IsValidHost(settings.Host)
            || !ConfigurationValidation.IsValidPort(settings.Port)
            || !Enum.IsDefined(settings.Security)
            || settings.TimeoutSeconds is < MinTimeoutSeconds or > MaxTimeoutSeconds
            || !ConfigurationValidation.IsValidEmail(settings.SenderAddress)
            || !ConfigurationValidation.IsValidSingleLine(settings.SenderName, 100)
            || !ConfigurationValidation.IsValidSingleLine(settings.Username, 256))
        {
            return false;
        }

        // Credentials need a password, and are never sent without encryption.
        return string.IsNullOrEmpty(settings.Username)
            || (!string.IsNullOrEmpty(settings.Password) && settings.Security != SmtpSecurityMode.None);
    }

    private static EmailTransportResult Map(Exception exception, Phase phase)
    {
        switch (exception)
        {
            case SmtpCommandException command:
                var status = (int)command.StatusCode;
                return command.ErrorCode switch
                {
                    SmtpErrorCode.RecipientNotAccepted => new EmailTransportResult(EmailTransportOutcome.RecipientRejected, status),
                    SmtpErrorCode.SenderNotAccepted => new EmailTransportResult(EmailTransportOutcome.SenderRejected, status),
                    _ => new EmailTransportResult(EmailTransportOutcome.UnexpectedError, status)
                };

            case MailKit.Security.AuthenticationException:
                return new EmailTransportResult(EmailTransportOutcome.AuthenticationFailed);

            case System.Security.Authentication.AuthenticationException:
                return new EmailTransportResult(EmailTransportOutcome.TlsFailed);

            case TimeoutException:
                return new EmailTransportResult(phase == Phase.Send ? EmailTransportOutcome.Uncertain : EmailTransportOutcome.Timeout);

            case NotSupportedException:
                // MailKit raises this when STARTTLS or AUTH is required but the server lacks it.
                return new EmailTransportResult(phase == Phase.Authenticate
                    ? EmailTransportOutcome.AuthenticationFailed
                    : EmailTransportOutcome.TlsFailed);

            case SslHandshakeException:
                return new EmailTransportResult(EmailTransportOutcome.TlsFailed);

            case SocketException:
            case IOException:
            case ServiceNotConnectedException:
            case ProtocolException:
                // Once sending has begun, a dropped connection means the outcome is unknown: the
                // server may have accepted the message just before the link failed.
                return new EmailTransportResult(phase switch
                {
                    Phase.Connect => EmailTransportOutcome.ConnectionFailed,
                    Phase.Send => EmailTransportOutcome.Uncertain,
                    _ => EmailTransportOutcome.UnexpectedError
                });

            default:
                return new EmailTransportResult(EmailTransportOutcome.UnexpectedError);
        }
    }

    private async Task<EmailTransportResult> RunAsync(SmtpSettings settings, OutgoingEmail? email, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (cancellationToken.IsCancellationRequested)
        {
            return new EmailTransportResult(EmailTransportOutcome.Cancelled);
        }

        if (!SettingsAreValid(settings))
        {
            _logger.LogWarning("SMTP request rejected before connecting: the settings are invalid.");
            return new EmailTransportResult(EmailTransportOutcome.InvalidRequest);
        }

        MimeKit.MimeMessage? message = null;
        if (email is not null)
        {
            try
            {
                message = MimeMessageFactory.Create(settings, email);
            }
            catch (ArgumentException)
            {
                _logger.LogWarning("SMTP request rejected before connecting: the message or recipient is invalid.");
                return new EmailTransportResult(EmailTransportOutcome.InvalidRequest);
            }
        }

        var phase = Phase.Connect;
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        budget.CancelAfter(TimeSpan.FromSeconds((double)settings.TimeoutSeconds * OperationBudgetFactor));

        try
        {
            using var client = new SmtpClient { Timeout = settings.TimeoutSeconds * 1000 };
            if (_testCertificateValidation is not null)
            {
                client.ServerCertificateValidationCallback = _testCertificateValidation;
            }

            await client.ConnectAsync(settings.Host, settings.Port, ToSocketOptions(settings.Security), budget.Token).ConfigureAwait(false);

            if (!string.IsNullOrEmpty(settings.Username))
            {
                phase = Phase.Authenticate;
                await client.AuthenticateAsync(settings.Username, settings.Password!, budget.Token).ConfigureAwait(false);
            }

            if (message is not null)
            {
                phase = Phase.Send;
                await client.SendAsync(message, budget.Token).ConfigureAwait(false);
            }

            try
            {
                await client.DisconnectAsync(true, budget.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The server already accepted everything; a failed goodbye changes nothing.
                _logger.LogDebug("SMTP disconnect failed after success ({ExceptionType}).", ex.GetType().Name);
            }

            _logger.LogInformation("SMTP {Operation} accepted by {Host}:{Port}.", message is null ? "connection test" : "message", settings.Host, settings.Port);
            return new EmailTransportResult(EmailTransportOutcome.Accepted);
        }
        catch (OperationCanceledException)
        {
            var outcome = cancellationToken.IsCancellationRequested
                ? EmailTransportOutcome.Cancelled
                : (phase == Phase.Send ? EmailTransportOutcome.Uncertain : EmailTransportOutcome.Timeout);
            _logger.LogWarning("SMTP operation to {Host}:{Port} ended: {Outcome}.", settings.Host, settings.Port, outcome);
            return new EmailTransportResult(outcome);
        }
        catch (Exception ex)
        {
            var result = Map(ex, phase);
            _logger.LogWarning(
                "SMTP operation to {Host}:{Port} failed in {Phase}: {Outcome} ({ExceptionType}).",
                settings.Host,
                settings.Port,
                phase,
                result.Outcome,
                ex.GetType().Name);
            return result;
        }
    }
}
