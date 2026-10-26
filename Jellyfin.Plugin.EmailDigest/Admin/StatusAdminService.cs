using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.EmailDigest.Configuration;
using Jellyfin.Plugin.EmailDigest.Delivery;
using Jellyfin.Plugin.EmailDigest.Services;

namespace Jellyfin.Plugin.EmailDigest.Admin;

/// <summary>
/// Reports whether the digest would run and what the last run did.
/// </summary>
public sealed class StatusAdminService
{
    private readonly ISmtpConfigurationAccessor _configuration;
    private readonly IRunSummaryStore _summaries;
    private readonly IUserPreferenceRepository _preferences;

    /// <summary>
    /// Initializes a new instance of the <see cref="StatusAdminService"/> class.
    /// </summary>
    /// <param name="configuration">Configuration access.</param>
    /// <param name="summaries">The last-run store.</param>
    /// <param name="preferences">Per-user preferences, used only to count them.</param>
    public StatusAdminService(ISmtpConfigurationAccessor configuration, IRunSummaryStore summaries, IUserPreferenceRepository preferences)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(summaries);
        ArgumentNullException.ThrowIfNull(preferences);
        _preferences = preferences;
        _configuration = configuration;
        _summaries = summaries;
    }

    /// <summary>
    /// Gets the status.
    /// </summary>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>200 with <see cref="DigestStatusDto"/>.</returns>
    public async Task<AdminResponse> GetStatusAsync(CancellationToken cancellationToken)
    {
        var config = _configuration.GetCopy();
        var last = await _summaries.GetAsync(cancellationToken).ConfigureAwait(false);
        var subscribers = await CountAsync(cancellationToken).ConfigureAwait(false);
        var publicAddress = PublicBaseUrl.TryNormalize(config.PublicBaseUrl);
        var mode = !config.DigestEnabled ? DigestRunMode.Disabled : (config.DigestDryRun ? DigestRunMode.DryRun : DigestRunMode.Live);

        return new AdminResponse(
            200,
            new DigestStatusDto(
                config.DigestEnabled,
                config.DigestDryRun,
                config.SmtpEnabled,
                config.MonitoredLibraries.Count,
                mode.ToString(),
                last is null ? null : ToDto(last),
                subscribers,
                subscribers is null,
                publicAddress is not null,
                publicAddress is not null && publicAddress.StartsWith("http://", StringComparison.OrdinalIgnoreCase)));
    }

    // Counts only. An unreadable file is reported as such rather than as zero subscribers.
    private async Task<SubscriberCountsDto?> CountAsync(CancellationToken cancellationToken)
    {
        try
        {
            var all = await _preferences.GetAllAsync(cancellationToken).ConfigureAwait(false);
            var wanting = all.Count(p => p.NotificationsEnabled);
            var receiving = all.Count(p => p.CanReceiveDigest());
            return new SubscriberCountsDto(all.Count, wanting, receiving, wanting - receiving);
        }
        catch (UserPreferencesException)
        {
            return null;
        }
    }

    private static LastRunDto ToDto(DigestRunSummary s)
    {
        return new LastRunDto(
            s.StartedUtc,
            s.FinishedUtc,
            s.Mode.ToString(),
            s.Outcome.ToString(),
            s.Reason,
            s.NeedsAttention,
            s.UsersEvaluated,
            s.UsersSkipped,
            s.MessagesAccepted,
            s.MessagesFailed,
            s.MessagesUncertain,
            s.UsersWithNoNewMedia,
            s.DigestsRenderedDryRun,
            s.UsersNotAttempted);
    }
}
