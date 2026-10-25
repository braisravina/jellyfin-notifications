namespace Jellyfin.Plugin.EmailDigest.Services;

/// <summary>
/// Where the SMTP password comes from.
/// </summary>
public enum SmtpPasswordSource
{
    /// <summary>
    /// No password is configured.
    /// </summary>
    None = 0,

    /// <summary>
    /// A password saved through the administrator page, kept in the plugin's own data folder.
    /// </summary>
    Stored = 1,

    /// <summary>
    /// A password supplied by the deployment, for example a Docker secret. It cannot be changed
    /// or cleared from the web interface.
    /// </summary>
    External = 2
}
