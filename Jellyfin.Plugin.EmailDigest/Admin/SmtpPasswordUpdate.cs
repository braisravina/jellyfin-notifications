namespace Jellyfin.Plugin.EmailDigest.Admin;

/// <summary>
/// A request to replace the stored SMTP password. Travels only in the request body.
/// </summary>
/// <param name="Password">The new password.</param>
public sealed record SmtpPasswordUpdate(string? Password)
{
    /// <summary>
    /// Describes the request without the password, so it is safe in logs and debugger views.
    /// </summary>
    /// <returns>A redacted description.</returns>
    public override string ToString()
    {
        return "SmtpPasswordUpdate { Password = <redacted> }";
    }
}
