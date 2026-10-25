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
public sealed record DigestStatusDto(bool DigestEnabled, bool DryRun, bool SmtpEnabled, int MonitoredLibraryCount, string Mode, LastRunDto? LastRun);
