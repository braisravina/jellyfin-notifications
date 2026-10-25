using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.EmailDigest.Delivery;

namespace Jellyfin.Plugin.EmailDigest.Admin;

/// <summary>
/// Reports whether the digest would run and what the last run did.
/// </summary>
public sealed class StatusAdminService
{
    private readonly ISmtpConfigurationAccessor _configuration;
    private readonly IRunSummaryStore _summaries;

    /// <summary>
    /// Initializes a new instance of the <see cref="StatusAdminService"/> class.
    /// </summary>
    /// <param name="configuration">Configuration access.</param>
    /// <param name="summaries">The last-run store.</param>
    public StatusAdminService(ISmtpConfigurationAccessor configuration, IRunSummaryStore summaries)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(summaries);
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
        var mode = !config.DigestEnabled ? DigestRunMode.Disabled : (config.DigestDryRun ? DigestRunMode.DryRun : DigestRunMode.Live);

        return new AdminResponse(
            200,
            new DigestStatusDto(
                config.DigestEnabled,
                config.DigestDryRun,
                config.SmtpEnabled,
                config.MonitoredLibraries.Count,
                mode.ToString(),
                last is null ? null : ToDto(last)));
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
