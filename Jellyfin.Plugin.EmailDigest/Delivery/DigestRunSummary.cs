using System;

namespace Jellyfin.Plugin.EmailDigest.Delivery;

/// <summary>
/// Counts describing one digest run. Holds no addresses, names, user ids, titles or message content.
/// </summary>
public sealed class DigestRunSummary
{
    /// <summary>
    /// Gets or sets when the run started.
    /// </summary>
    public DateTimeOffset StartedUtc { get; set; }

    /// <summary>
    /// Gets or sets when the run finished.
    /// </summary>
    public DateTimeOffset FinishedUtc { get; set; }

    /// <summary>
    /// Gets or sets the fixed upper bound of the media window this run examined.
    /// </summary>
    public DateTimeOffset UpperBoundUtc { get; set; }

    /// <summary>
    /// Gets or sets the mode the run used.
    /// </summary>
    public DigestRunMode Mode { get; set; }

    /// <summary>
    /// Gets or sets how the run ended.
    /// </summary>
    public DigestRunOutcome Outcome { get; set; }

    /// <summary>
    /// Gets or sets a short machine-readable reason for a skipped or aborted run, or empty.
    /// </summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the number of users with notifications on that the run looked at.
    /// </summary>
    public int UsersEvaluated { get; set; }

    /// <summary>
    /// Gets or sets the number of users passed over (notifications off, unknown or disabled account,
    /// invalid address).
    /// </summary>
    public int UsersSkipped { get; set; }

    /// <summary>
    /// Gets or sets the number of messages the SMTP server accepted.
    /// </summary>
    public int MessagesAccepted { get; set; }

    /// <summary>
    /// Gets or sets the number of users whose delivery failed.
    /// </summary>
    public int MessagesFailed { get; set; }

    /// <summary>
    /// Gets or sets the number of users whose delivery outcome is unknown (counted in addition to failures).
    /// </summary>
    public int MessagesUncertain { get; set; }

    /// <summary>
    /// Gets or sets the number of messages accepted whose delivery could not be recorded; the user
    /// will probably receive a duplicate on the next run.
    /// </summary>
    public int AcceptedButNotRecorded { get; set; }

    /// <summary>
    /// Gets or sets the number of users with nothing new.
    /// </summary>
    public int UsersWithNoNewMedia { get; set; }

    /// <summary>
    /// Gets or sets the number of digests built and rendered in a dry run.
    /// </summary>
    public int DigestsRenderedDryRun { get; set; }

    /// <summary>
    /// Gets or sets the number of new users given a starting baseline without a message.
    /// </summary>
    public int BaselinesRecorded { get; set; }

    /// <summary>
    /// Gets or sets the number of users never attempted because the run stopped.
    /// </summary>
    public int UsersNotAttempted { get; set; }

    /// <summary>
    /// Gets a value indicating whether anything went wrong that an administrator should look at.
    /// </summary>
    public bool NeedsAttention => Outcome is DigestRunOutcome.CompletedWithFailures or DigestRunOutcome.Aborted;
}
