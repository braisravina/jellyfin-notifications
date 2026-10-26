using System;
using System.Linq;
using Jellyfin.Plugin.EmailDigest.ScheduledTasks;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.EmailDigest.Delivery;

/// <summary>
/// <see cref="IScheduleInfo"/> backed by Jellyfin's task manager.
/// </summary>
public sealed class JellyfinScheduleInfo : IScheduleInfo
{
    private readonly ITaskManager _tasks;
    private readonly TimeProvider _time;

    /// <summary>
    /// Initializes a new instance of the <see cref="JellyfinScheduleInfo"/> class.
    /// </summary>
    /// <param name="tasks">Jellyfin's task manager.</param>
    /// <param name="time">The clock.</param>
    public JellyfinScheduleInfo(ITaskManager tasks, TimeProvider time)
    {
        _tasks = tasks ?? throw new ArgumentNullException(nameof(tasks));
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    /// <inheritdoc />
    public DateTimeOffset? GetNextRunUtc()
    {
        var worker = _tasks.ScheduledTasks.FirstOrDefault(w => string.Equals(w.ScheduledTask.Key, SendEmailDigestTask.TaskKey, StringComparison.Ordinal));
        if (worker is null)
        {
            return null;
        }

        var end = worker.LastExecutionResult?.EndTimeUtc;
        DateTimeOffset? lastEnd = end is { } value && value != default ? new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)) : null;
        return NextRunCalculator.Next(worker.Triggers, _time.GetUtcNow(), lastEnd, TimeZoneInfo.Local);
    }
}
