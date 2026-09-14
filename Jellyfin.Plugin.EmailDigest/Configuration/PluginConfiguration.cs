using System.Collections.ObjectModel;
using Jellyfin.Plugin.EmailDigest.Localization;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.EmailDigest.Configuration;

/// <summary>
/// Plugin configuration for Jellyfin Email Digest.
/// </summary>
/// <remarks>
/// Persisted by Jellyfin's built-in <c>IXmlSerializer</c> as plain, unencrypted XML under the
/// server's plugin configuration directory. There is no Jellyfin-provided encrypted secret store
/// for plugins as of 10.11.8, so <see cref="SmtpPassword"/> is stored in plaintext at rest — see
/// the README for the operational implications. <see cref="SmtpHost"/> through
/// <see cref="SenderDisplayName"/> define the data shape now (Phase 2); the admin UI for them,
/// including password masking, is added in Phase 5 alongside actual SMTP sending, so the fields
/// are never rendered to the browser in the interim.
/// </remarks>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PluginConfiguration"/> class.
    /// </summary>
    public PluginConfiguration()
    {
        SmtpEnabled = false;
        SmtpHost = string.Empty;
        SmtpPort = 587;
        SmtpSecurity = SmtpSecurityMode.StartTls;
        SmtpUsername = string.Empty;
        SmtpPassword = string.Empty;
        SenderEmailAddress = string.Empty;
        SenderDisplayName = "Jellyfin";

        DigestEnabled = false;
        DefaultLanguage = SupportedLanguages.Default;
        FallbackLookbackHours = 168; // 7 days
    }

    /// <summary>
    /// Gets or sets a value indicating whether SMTP delivery is enabled.
    /// </summary>
    public bool SmtpEnabled { get; set; }

    /// <summary>
    /// Gets or sets the SMTP server host name.
    /// </summary>
    public string SmtpHost { get; set; }

    /// <summary>
    /// Gets or sets the SMTP server port.
    /// </summary>
    public int SmtpPort { get; set; }

    /// <summary>
    /// Gets or sets the SMTP transport security mode.
    /// </summary>
    public SmtpSecurityMode SmtpSecurity { get; set; }

    /// <summary>
    /// Gets or sets the SMTP authentication username.
    /// </summary>
    public string SmtpUsername { get; set; }

    /// <summary>
    /// Gets or sets the SMTP authentication password.
    /// </summary>
    /// <remarks>
    /// Stored in plaintext XML (see class remarks). Not yet exposed on the configuration page;
    /// masked read/write access is added in Phase 5.
    /// </remarks>
    public string SmtpPassword { get; set; }

    /// <summary>
    /// Gets or sets the sender email address digests are sent from.
    /// </summary>
    public string SenderEmailAddress { get; set; }

    /// <summary>
    /// Gets or sets the sender display name digests are sent from.
    /// </summary>
    public string SenderDisplayName { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the digest feature is globally enabled.
    /// </summary>
    public bool DigestEnabled { get; set; }

    /// <summary>
    /// Gets or sets the default language used when a user has no preference recorded.
    /// </summary>
    public string DefaultLanguage { get; set; }

    /// <summary>
    /// Gets or sets the lookback window, in hours, used to find "new" media on the very first
    /// digest run, before any checkpoint exists.
    /// </summary>
    public int FallbackLookbackHours { get; set; }

    /// <summary>
    /// Gets the Jellyfin libraries monitored for new media, and which digest section each is
    /// grouped under. Populated in place (add/remove/clear), not by reassignment, so the XML
    /// serializer can round-trip it without a public setter.
    /// </summary>
    public Collection<MonitoredLibrary> MonitoredLibraries { get; } = new();
}
