namespace Jellyfin.Plugin.EmailDigest.Admin;

/// <summary>
/// The state of the digest feature and its last run, for the administrator page. Counts only.
/// </summary>
/// <param name="DigestEnabled">Whether the digest is enabled.</param>
/// <param name="DryRun">Whether runs only build and render.</param>
/// <param name="SmtpEnabled">Whether SMTP delivery is enabled.</param>
/// <param name="MonitoredLibraryCount">How many libraries are monitored.</param>
/// <param name="Mode">"Disabled", "DryRun" or "Live": what a run would do now.</param>
/// <param name="LastRun">The last recorded run, or null.</param>
/// <param name="Subscribers">Counts of people with preferences, or null when the preferences file cannot be read.</param>
/// <param name="PreferencesUnreadable">Whether the preferences file cannot be trusted.</param>
/// <param name="PublicAddressSet">Whether a usable public server address is configured; address verification and live sending need it.</param>
/// <param name="PublicAddressInsecure">Whether that address is plain http, so links in emails (which carry one-time tokens) travel unencrypted.</param>
public sealed record DigestStatusDto(
    bool DigestEnabled,
    bool DryRun,
    bool SmtpEnabled,
    int MonitoredLibraryCount,
    string Mode,
    LastRunDto? LastRun,
    SubscriberCountsDto? Subscribers,
    bool PreferencesUnreadable,
    bool PublicAddressSet,
    bool PublicAddressInsecure);
