using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.EmailDigest.ScheduledTasks;

/// <summary>
/// Native Jellyfin scheduled task that sends the email digest to authorized users.
/// </summary>
/// <remarks>
/// Phase 1 stub: registers with Jellyfin's Scheduled Tasks dashboard and completes
/// immediately. Checkpointing, media discovery, rendering and SMTP delivery are added
/// in later phases.
/// </remarks>
public class SendEmailDigestTask : IScheduledTask
{
    private readonly ILogger<SendEmailDigestTask> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SendEmailDigestTask"/> class.
    /// </summary>
    /// <param name="logger">Instance of the <see cref="ILogger{SendEmailDigestTask}"/> interface.</param>
    public SendEmailDigestTask(ILogger<SendEmailDigestTask> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Send Email Digest";

    /// <inheritdoc />
    public string Key => "EmailDigestSendTask";

    /// <inheritdoc />
    public string Description => "Sends a multilingual email digest of newly added media to authorized users.";

    /// <inheritdoc />
    public string Category => "Email Digest";

    /// <inheritdoc />
    public Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Email Digest scheduled task executed (Phase 1 stub: no media processed, no email sent)");
        progress.Report(100);
        return Task.CompletedTask;
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
