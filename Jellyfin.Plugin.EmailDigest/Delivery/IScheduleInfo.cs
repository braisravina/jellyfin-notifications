using System;

namespace Jellyfin.Plugin.EmailDigest.Delivery;

/// <summary>
/// Tells when the digest task will next run.
/// </summary>
public interface IScheduleInfo
{
    /// <summary>
    /// Gets the next scheduled start of the digest task.
    /// </summary>
    /// <returns>The time, or null when the task has no determinable schedule.</returns>
    DateTimeOffset? GetNextRunUtc();
}
