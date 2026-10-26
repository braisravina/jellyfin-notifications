using System;
using System.Collections.Generic;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.EmailDigest.Delivery;

/// <summary>
/// Works out when a scheduled task will next start, from its triggers.
/// </summary>
/// <remarks>
/// Jellyfin keeps daily and weekly trigger times as a time of day in the server's local time zone, so
/// the zone is a parameter. Startup triggers have no time and are ignored. An interval trigger is
/// counted from the end of the last run; with no last run there is no honest answer, so none is given.
/// A schedule says when the task runs, never that a digest will be sent: that depends on there being
/// something new.
/// </remarks>
public static class NextRunCalculator
{
    /// <summary>
    /// Finds the earliest next start.
    /// </summary>
    /// <param name="triggers">The task's triggers.</param>
    /// <param name="nowUtc">The current time.</param>
    /// <param name="lastRunEndUtc">When the task last finished, if it ever has.</param>
    /// <param name="zone">The server's local time zone.</param>
    /// <returns>The next start, or null when none can be determined.</returns>
    public static DateTimeOffset? Next(IEnumerable<TaskTriggerInfo> triggers, DateTimeOffset nowUtc, DateTimeOffset? lastRunEndUtc, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(triggers);
        ArgumentNullException.ThrowIfNull(zone);

        DateTimeOffset? best = null;
        foreach (var trigger in triggers)
        {
            var next = trigger.Type switch
            {
                TaskTriggerInfoType.DailyTrigger => Daily(trigger, nowUtc, zone),
                TaskTriggerInfoType.WeeklyTrigger => Weekly(trigger, nowUtc, zone),
                TaskTriggerInfoType.IntervalTrigger => Interval(trigger, nowUtc, lastRunEndUtc),
                _ => null
            };

            if (next is not null && (best is null || next < best))
            {
                best = next;
            }
        }

        return best;
    }

    private static DateTimeOffset? Daily(TaskTriggerInfo trigger, DateTimeOffset nowUtc, TimeZoneInfo zone)
    {
        if (!ValidTimeOfDay(trigger.TimeOfDayTicks, out var timeOfDay))
        {
            return null;
        }

        var localNow = TimeZoneInfo.ConvertTime(nowUtc, zone).DateTime;
        for (var day = 0; day <= 2; day++)
        {
            var candidate = At(localNow.Date.AddDays(day), timeOfDay, zone);
            if (candidate > nowUtc)
            {
                return candidate;
            }
        }

        return null;
    }

    private static DateTimeOffset? Weekly(TaskTriggerInfo trigger, DateTimeOffset nowUtc, TimeZoneInfo zone)
    {
        if (trigger.DayOfWeek is null || !ValidTimeOfDay(trigger.TimeOfDayTicks, out var timeOfDay))
        {
            return null;
        }

        var localNow = TimeZoneInfo.ConvertTime(nowUtc, zone).DateTime;
        for (var day = 0; day <= 8; day++)
        {
            var date = localNow.Date.AddDays(day);
            if (date.DayOfWeek != trigger.DayOfWeek)
            {
                continue;
            }

            var candidate = At(date, timeOfDay, zone);
            if (candidate > nowUtc)
            {
                return candidate;
            }
        }

        return null;
    }

    private static DateTimeOffset? Interval(TaskTriggerInfo trigger, DateTimeOffset nowUtc, DateTimeOffset? lastRunEndUtc)
    {
        if (trigger.IntervalTicks is not > 0 || lastRunEndUtc is null)
        {
            return null;
        }

        var next = lastRunEndUtc.Value + TimeSpan.FromTicks(trigger.IntervalTicks.Value);
        return next > nowUtc ? next : nowUtc;
    }

    private static bool ValidTimeOfDay(long? ticks, out TimeSpan timeOfDay)
    {
        timeOfDay = TimeSpan.Zero;
        if (ticks is null || ticks < 0 || ticks >= TimeSpan.TicksPerDay)
        {
            return false;
        }

        timeOfDay = TimeSpan.FromTicks(ticks.Value);
        return true;
    }

    // A local time that does not exist (the hour skipped when clocks go forward) runs an hour later;
    // one that happens twice (clocks go back) uses the zone's own choice.
    private static DateTimeOffset At(DateTime localDate, TimeSpan timeOfDay, TimeZoneInfo zone)
    {
        var local = DateTime.SpecifyKind(localDate.Date + timeOfDay, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local))
        {
            local = local.AddHours(1);
        }

        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone), TimeSpan.Zero);
    }
}
