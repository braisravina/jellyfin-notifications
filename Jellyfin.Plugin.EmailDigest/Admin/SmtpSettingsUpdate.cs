namespace Jellyfin.Plugin.EmailDigest.Admin;

/// <summary>
/// A request to change the SMTP settings. Deliberately has no password member: passwords are only
/// accepted by the dedicated password endpoint, so the settings round trip can never carry one.
/// </summary>
/// <param name="SmtpEnabled">Whether SMTP delivery is enabled.</param>
/// <param name="Host">The server host.</param>
/// <param name="Port">The server port.</param>
/// <param name="Security">"None", "StartTls" or "ImplicitTls".</param>
/// <param name="Username">The login name, or empty.</param>
/// <param name="SenderAddress">The sender address.</param>
/// <param name="SenderName">The sender display name.</param>
/// <param name="TimeoutSeconds">The timeout in seconds.</param>
public sealed record SmtpSettingsUpdate(
    bool SmtpEnabled,
    string? Host,
    int Port,
    string? Security,
    string? Username,
    string? SenderAddress,
    string? SenderName,
    int TimeoutSeconds);
