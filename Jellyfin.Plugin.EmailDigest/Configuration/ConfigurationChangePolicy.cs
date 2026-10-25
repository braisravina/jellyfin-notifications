using System;

namespace Jellyfin.Plugin.EmailDigest.Configuration;

/// <summary>
/// Rules about what a configuration change implies beyond validity.
/// </summary>
public static class ConfigurationChangePolicy
{
    /// <summary>
    /// Decides whether the stored SMTP password must be discarded because the server it would be
    /// sent to has changed.
    /// </summary>
    /// <remarks>
    /// A password saved for one server must never be sent to another. If the host, port or username
    /// changes, whoever made the change must enter the password again. This is applied inside
    /// <c>Plugin.UpdateConfiguration</c>, so it also covers Jellyfin's generic configuration
    /// endpoint, not only the plugin's own administrator API. A password supplied by the deployment
    /// (a Docker secret) is not affected: it is chosen by whoever deploys the server.
    /// </remarks>
    /// <param name="current">The configuration in force.</param>
    /// <param name="proposed">The configuration about to be saved.</param>
    /// <returns><see langword="true"/> when the stored password must be cleared.</returns>
    public static bool MustClearStoredPassword(PluginConfiguration current, PluginConfiguration proposed)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(proposed);

        return !string.Equals(current.SmtpHost.Trim(), proposed.SmtpHost.Trim(), StringComparison.OrdinalIgnoreCase)
               || current.SmtpPort != proposed.SmtpPort
               || !string.Equals(current.SmtpUsername.Trim(), proposed.SmtpUsername.Trim(), StringComparison.Ordinal);
    }
}
