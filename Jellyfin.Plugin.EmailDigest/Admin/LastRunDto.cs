using System;

namespace Jellyfin.Plugin.EmailDigest.Admin;

/// <summary>
/// The counts of one digest run. Holds no addresses, names, user ids or titles.
/// </summary>
/// <param name="StartedUtc">When the run started.</param>
/// <param name="FinishedUtc">When it finished.</param>
/// <param name="Mode">"Disabled", "DryRun" or "Live".</param>
/// <param name="Outcome">"Completed", "CompletedWithFailures", "Aborted", "Cancelled" or "Skipped".</param>
/// <param name="Reason">A short reason code, or empty.</param>
/// <param name="NeedsAttention">True when the run failed in part or in full.</param>
/// <param name="UsersEvaluated">Users looked at.</param>
/// <param name="UsersSkipped">Users passed over.</param>
/// <param name="MessagesAccepted">Messages the server accepted.</param>
/// <param name="MessagesFailed">Users whose delivery failed.</param>
/// <param name="MessagesUncertain">Users whose delivery outcome is unknown.</param>
/// <param name="UsersWithNoNewMedia">Users with nothing new.</param>
/// <param name="DigestsRenderedDryRun">Digests built in a dry run.</param>
/// <param name="UsersNotAttempted">Users never attempted because the run stopped.</param>
public sealed record LastRunDto(
    DateTimeOffset StartedUtc,
    DateTimeOffset FinishedUtc,
    string Mode,
    string Outcome,
    string Reason,
    bool NeedsAttention,
    int UsersEvaluated,
    int UsersSkipped,
    int MessagesAccepted,
    int MessagesFailed,
    int MessagesUncertain,
    int UsersWithNoNewMedia,
    int DigestsRenderedDryRun,
    int UsersNotAttempted);
