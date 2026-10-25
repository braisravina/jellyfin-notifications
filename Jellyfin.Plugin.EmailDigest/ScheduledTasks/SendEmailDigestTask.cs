using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.EmailDigest.Delivery;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.EmailDigest.ScheduledTasks;

/// <summary>
/// Native Jellyfin scheduled task that sends the email digest to authorized users.
/// </summary>
/// <remarks>
/// A thin shell around <see cref="DigestRunner"/>. The scheduled run and a manual run from Jellyfin's
/// Scheduled Tasks page take exactly the same path, so both obey the same safeguards: nothing happens
/// while the digest is disabled, dry run (on by default) sends nothing, and two runs never overlap.
/// A run that ends with failures or is aborted makes the task throw, so Jellyfin shows it as failed
/// rather than successful; the exception carries counts and a reason code only.
/// </remarks>
public class SendEmailDigestTask : IScheduledTask
{
    private readonly DigestRunner _runner;
    private readonly ILogger<SendEmailDigestTask> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SendEmailDigestTask"/> class.
    /// </summary>
    /// <param name="runner">The digest runner.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{SendEmailDigestTask}"/> interface.</param>
    public SendEmailDigestTask(DigestRunner runner, ILogger<SendEmailDigestTask> logger)
    {
        ArgumentNullException.ThrowIfNull(runner);
        _runner = runner;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Send Email Digest";

    /// <inheritdoc />
    public string Key => "EmailDigestSendTask";

    /// <inheritdoc />
    public string Description => "Sends a multilingual email digest of newly added media to authorized users. Does nothing while the digest is disabled, and sends nothing in dry-run mode.";

    /// <inheritdoc />
    public string Category => "Email Digest";

    /// <inheritdoc />
    /// <exception cref="DigestRunException">The run ended with failures or was aborted.</exception>
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        progress.Report(0);
        var summary = await _runner.RunAsync(cancellationToken).ConfigureAwait(false);
        progress.Report(100);

        switch (summary.Outcome)
        {
            case DigestRunOutcome.Cancelled:
                throw new OperationCanceledException(cancellationToken);
            case DigestRunOutcome.CompletedWithFailures:
            case DigestRunOutcome.Aborted:
                _logger.LogWarning("The Email Digest task is reporting failure: {Outcome} ({Reason}).", summary.Outcome, summary.Reason);
                throw new DigestRunException(string.Format(
                    CultureInfo.InvariantCulture,
                    "Email Digest run {0} ({1}): {2} accepted, {3} failed ({4} uncertain), {5} not attempted. See the Jellyfin log.",
                    summary.Outcome,
                    summary.Reason,
                    summary.MessagesAccepted,
                    summary.MessagesFailed,
                    summary.MessagesUncertain,
                    summary.UsersNotAttempted));
            default:
                return;
        }
    }

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        return
        [
            new TaskTriggerInfo
            {
                Type = TaskTriggerInfoType.DailyTrigger,
                TimeOfDayTicks = TimeSpan.FromHours(8).Ticks
            }
        ];
    }
}
