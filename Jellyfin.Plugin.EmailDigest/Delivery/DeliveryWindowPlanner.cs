using System;

namespace Jellyfin.Plugin.EmailDigest.Delivery;

/// <summary>
/// Decides the discovery window for one user. Pure and deterministic.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><description>The upper bound is fixed by the caller once per run, so media added while the run
/// is in progress falls into the next run's window instead of being skipped.</description></item>
/// <item><description>A user with state continues from their own checkpoint. It is never moved backwards.</description></item>
/// <item><description>A new user (no state) gets the bounded fallback lookback only, never the whole
/// library. A lookback of 0 means "start from now": the first run sends nothing and only records
/// the baseline. A checkpoint left by an older version, if there is one, is used instead.</description></item>
/// <item><description>A user whose checkpoint is older than <see cref="MaxCatchUp"/> (for example after
/// a long pause with notifications off) is caught up only that far; older media is deliberately
/// not announced. Until then a paused user's checkpoint is left alone, so nothing pending is lost.</description></item>
/// </list>
/// </remarks>
public static class DeliveryWindowPlanner
{
    /// <summary>
    /// The longest span a single digest ever reaches back.
    /// </summary>
    public static readonly TimeSpan MaxCatchUp = TimeSpan.FromDays(30);

    /// <summary>
    /// Plans the window.
    /// </summary>
    /// <param name="state">The user's state, or null for a new user.</param>
    /// <param name="legacyCheckpoint">A checkpoint from before per-user state, or null.</param>
    /// <param name="fallbackLookbackHours">The configured lookback for a new user.</param>
    /// <param name="upperBoundUtc">The run's fixed inclusive upper bound.</param>
    /// <returns>The window.</returns>
    public static DeliveryWindow Plan(
        UserDeliveryState? state,
        DateTimeOffset? legacyCheckpoint,
        int fallbackLookbackHours,
        DateTimeOffset upperBoundUtc)
    {
        DateTimeOffset from;
        DeliveryWindowSource source;

        if (state is not null)
        {
            from = state.CheckpointUtc;
            source = DeliveryWindowSource.Checkpoint;
        }
        else if (legacyCheckpoint is { } legacy)
        {
            from = legacy;
            source = DeliveryWindowSource.LegacyCheckpoint;
        }
        else
        {
            var hours = Math.Clamp(fallbackLookbackHours, 0, (int)MaxCatchUp.TotalHours);
            from = upperBoundUtc - TimeSpan.FromHours(hours);
            source = DeliveryWindowSource.NewUserBaseline;
        }

        var earliest = upperBoundUtc - MaxCatchUp;
        if (from < earliest)
        {
            from = earliest;
            source = DeliveryWindowSource.CatchUpLimit;
        }

        return new DeliveryWindow(from, upperBoundUtc, source);
    }
}
