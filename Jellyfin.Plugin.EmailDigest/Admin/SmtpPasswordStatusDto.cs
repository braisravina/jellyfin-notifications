namespace Jellyfin.Plugin.EmailDigest.Admin;

/// <summary>
/// Whether an SMTP password exists. Never the password.
/// </summary>
/// <param name="IsSet">True when a password is available.</param>
/// <param name="Source">"None", "Stored" or "External" (managed by the deployment, read-only here).</param>
public sealed record SmtpPasswordStatusDto(bool IsSet, string Source);
