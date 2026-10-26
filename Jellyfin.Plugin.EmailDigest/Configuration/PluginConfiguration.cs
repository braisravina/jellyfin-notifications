using System.Collections.ObjectModel;
using Jellyfin.Plugin.EmailDigest.Localization;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.EmailDigest.Configuration;

/// <summary>
/// Plugin configuration for Jellyfin Email Digest.
/// </summary>
/// <remarks>
/// Persisted by Jellyfin's built-in <c>IXmlSerializer</c> as plain, unencrypted XML under the
/// server's plugin configuration directory, and returned verbatim, as JSON, by Jellyfin's generic
/// <c>GET /Plugins/{id}/Configuration</c> endpoint. It therefore must never hold a secret: the SMTP
/// password is deliberately not a property of this class and lives in
/// <see cref="Services.ISmtpPasswordStore"/>, reachable only through the purpose-built
/// administrator endpoints. Everything here is safe to show to an administrator's browser.
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
        SenderEmailAddress = string.Empty;
        SenderDisplayName = "Jellyfin";
        SmtpTimeoutSeconds = 30;

        DigestEnabled = false;
        DefaultLanguage = SupportedLanguages.Default;
        FallbackLookbackHours = 168; // 7 days
        DigestDryRun = true;
        MaxEntriesPerSection = 25;
        MaxEntriesPerDigest = 100;
        MaxEpisodesPerSeries = 10;

        PublicBaseUrl = string.Empty;
        VerificationLinkMinutes = 60;
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
    /// Gets or sets the SMTP connect and send timeout, in seconds.
    /// </summary>
    public int SmtpTimeoutSeconds { get; set; }

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
    /// Gets or sets a value indicating whether a digest run only builds and renders digests,
    /// logging counts, without sending anything or recording any delivery. On by default, so
    /// enabling the digest never starts real sending by itself.
    /// </summary>
    public bool DigestDryRun { get; set; }

    /// <summary>
    /// Gets or sets the most entries (a movie, an album or a whole series) shown per category.
    /// Entries beyond the limit are not lost: they stay pending for the next digest.
    /// </summary>
    public int MaxEntriesPerSection { get; set; }

    /// <summary>
    /// Gets or sets the most entries shown in one digest across all categories.
    /// </summary>
    public int MaxEntriesPerDigest { get; set; }

    /// <summary>
    /// Gets or sets the most episodes listed under one series; the series line still states the
    /// full count.
    /// </summary>
    public int MaxEpisodesPerSeries { get; set; }

    /// <summary>
    /// Gets or sets the address users reach this server at from outside, for example
    /// <c>https://jellyfin.example.org</c> (include a sub-path if the server runs under one). Links in
    /// emails are built from it and from nothing else: never from a request, whose <c>Host</c> header a
    /// caller controls. Empty disables address verification and live digests, which need such links.
    /// </summary>
    public string PublicBaseUrl { get; set; }

    /// <summary>
    /// Gets or sets how many minutes an address-verification link works for.
    /// </summary>
    public int VerificationLinkMinutes { get; set; }

    /// <summary>
    /// Gets the Jellyfin libraries monitored for new media, and which digest section each is
    /// grouped under. Populated in place (add/remove/clear), not by reassignment, so the XML
    /// serializer can round-trip it without a public setter.
    /// </summary>
    public Collection<MonitoredLibrary> MonitoredLibraries { get; } = new();
}
