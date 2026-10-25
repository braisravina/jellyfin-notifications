namespace Jellyfin.Plugin.EmailDigest.Delivery;

/// <summary>
/// How a digest run ended.
/// </summary>
public enum DigestRunOutcome
{
    /// <summary>
    /// Every user was handled without a failure.
    /// </summary>
    Completed = 0,

    /// <summary>
    /// The run finished but at least one user failed, was left uncertain, or could not be recorded.
    /// It must be reported as a partial failure, never as plain success.
    /// </summary>
    CompletedWithFailures = 1,

    /// <summary>
    /// The run stopped early because continuing would be unsafe or pointless (see the abort reason).
    /// </summary>
    Aborted = 2,

    /// <summary>
    /// The run was cancelled.
    /// </summary>
    Cancelled = 3,

    /// <summary>
    /// Nothing was done: the digest is disabled, another run is in progress, or no library is monitored.
    /// </summary>
    Skipped = 4
}
