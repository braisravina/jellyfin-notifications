namespace Jellyfin.Plugin.EmailDigest.Admin;

/// <summary>
/// The SMTP settings as shown to an administrator. Contains no secret by construction: the
/// password is represented only by <see cref="Password"/>, a status.
/// </summary>
/// <param name="SmtpEnabled">Whether SMTP delivery is enabled.</param>
/// <param name="Host">The server host.</param>
/// <param name="Port">The server port.</param>
/// <param name="Security">"None", "StartTls" or "ImplicitTls".</param>
/// <param name="Username">The login name, or empty.</param>
/// <param name="SenderAddress">The sender address.</param>
/// <param name="SenderName">The sender display name.</param>
/// <param name="TimeoutSeconds">The timeout in seconds.</param>
/// <param name="Password">Whether a password exists and where it comes from.</param>
public sealed record SmtpSettingsDto(
    bool SmtpEnabled,
    string Host,
    int Port,
    string Security,
    string Username,
    string SenderAddress,
    string SenderName,
    int TimeoutSeconds,
    SmtpPasswordStatusDto Password);
