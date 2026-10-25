using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.EmailDigest.Delivery;

/// <summary>
/// How often and how patiently one message is retried after a transient failure. Bounded: there is
/// no unbounded or background retrying; whatever remains is picked up by the next scheduled run.
/// </summary>
/// <param name="MaxAttempts">The most tries per message, including the first.</param>
/// <param name="Backoff">The wait before each retry (entry 0 before the second try, and so on).</param>
public sealed record DigestRetryPolicy(int MaxAttempts, IReadOnlyList<TimeSpan> Backoff)
{
    /// <summary>
    /// Gets the default: three tries, waiting 2 seconds and then 10 seconds.
    /// </summary>
    public static DigestRetryPolicy Default { get; } = new(3, new[] { TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10) });

    /// <summary>
    /// Gets the wait before the given retry.
    /// </summary>
    /// <param name="retryNumber">1 for the first retry.</param>
    /// <returns>The wait; the last configured value repeats if the list is shorter.</returns>
    public TimeSpan DelayBefore(int retryNumber)
    {
        if (Backoff.Count == 0)
        {
            return TimeSpan.Zero;
        }

        return Backoff[Math.Min(Math.Max(retryNumber, 1) - 1, Backoff.Count - 1)];
    }
}
