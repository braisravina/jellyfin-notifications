namespace Jellyfin.Plugin.EmailDigest.Services;

/// <summary>
/// Whether an SMTP password exists, and where it comes from. Never the password itself.
/// </summary>
/// <param name="IsSet">True when a non-empty password is available.</param>
/// <param name="Source">Where it comes from.</param>
public readonly record struct SmtpPasswordStatus(bool IsSet, SmtpPasswordSource Source);
