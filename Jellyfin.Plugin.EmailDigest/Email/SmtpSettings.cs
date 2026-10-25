using Jellyfin.Plugin.EmailDigest.Configuration;

namespace Jellyfin.Plugin.EmailDigest.Email;

/// <summary>
/// Everything the transport needs to reach an SMTP server, including the password.
/// </summary>
/// <remarks>
/// Internal plumbing: it is built in memory for one operation and never serialized, logged or
/// returned over HTTP. <see cref="ToString"/> redacts the password so an accidental log line or
/// debugger view cannot leak it.
/// </remarks>
public sealed class SmtpSettings
{
    /// <summary>
    /// Gets the SMTP server host.
    /// </summary>
    public string Host { get; init; } = string.Empty;

    /// <summary>
    /// Gets the SMTP server port.
    /// </summary>
    public int Port { get; init; }

    /// <summary>
    /// Gets the transport security mode.
    /// </summary>
    public SmtpSecurityMode Security { get; init; } = SmtpSecurityMode.StartTls;

    /// <summary>
    /// Gets the login name, or empty for no authentication.
    /// </summary>
    public string Username { get; init; } = string.Empty;

    /// <summary>
    /// Gets the login password, or null when none is configured.
    /// </summary>
    public string? Password { get; init; }

    /// <summary>
    /// Gets the sender address.
    /// </summary>
    public string SenderAddress { get; init; } = string.Empty;

    /// <summary>
    /// Gets the sender display name.
    /// </summary>
    public string SenderName { get; init; } = string.Empty;

    /// <summary>
    /// Gets the connect and per-operation timeout in seconds.
    /// </summary>
    public int TimeoutSeconds { get; init; } = 30;

    /// <summary>
    /// Builds settings from the saved configuration and a password.
    /// </summary>
    /// <param name="configuration">The saved configuration.</param>
    /// <param name="password">The password from the secret store, or null.</param>
    /// <returns>The settings.</returns>
    public static SmtpSettings From(PluginConfiguration configuration, string? password)
    {
        System.ArgumentNullException.ThrowIfNull(configuration);
        return new SmtpSettings
        {
            Host = configuration.SmtpHost,
            Port = configuration.SmtpPort,
            Security = configuration.SmtpSecurity,
            Username = configuration.SmtpUsername,
            Password = password,
            SenderAddress = configuration.SenderEmailAddress,
            SenderName = configuration.SenderDisplayName,
            TimeoutSeconds = configuration.SmtpTimeoutSeconds
        };
    }

    /// <summary>
    /// Describes the settings without the password.
    /// </summary>
    /// <returns>A redacted description.</returns>
    public override string ToString()
    {
        return $"{Host}:{Port} {Security} user={(string.IsNullOrEmpty(Username) ? "<none>" : "<set>")} password=<redacted> timeout={TimeoutSeconds}s";
    }
}
