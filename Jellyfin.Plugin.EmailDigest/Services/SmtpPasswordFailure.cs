namespace Jellyfin.Plugin.EmailDigest.Services;

/// <summary>
/// Why an SMTP password change was refused.
/// </summary>
public enum SmtpPasswordFailure
{
    /// <summary>
    /// The value is empty, too long, or contains control characters.
    /// </summary>
    Invalid = 0,

    /// <summary>
    /// The password is managed by the deployment and cannot be changed here.
    /// </summary>
    ManagedExternally = 1
}
