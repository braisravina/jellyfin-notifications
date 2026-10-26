using System;
using System.Collections.Generic;
using Jellyfin.Plugin.EmailDigest.Delivery;
using Jellyfin.Plugin.EmailDigest.ScheduledTasks;
using MediaBrowser.Model.Tasks;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.EmailDigest.Tests.Delivery;

public sealed class NextRunCalculatorTests
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

    private static DateTimeOffset At(int month, int day, int hour, int minute = 0) => new(2026, month, day, hour, minute, 0, TimeSpan.Zero);

    private static TaskTriggerInfo Daily(int hour, int minute = 0) =>
        new() { Type = TaskTriggerInfoType.DailyTrigger, TimeOfDayTicks = new TimeSpan(hour, minute, 0).Ticks };

    private static TaskTriggerInfo Weekly(DayOfWeek day, int hour) =>
        new() { Type = TaskTriggerInfoType.WeeklyTrigger, DayOfWeek = day, TimeOfDayTicks = TimeSpan.FromHours(hour).Ticks };

    private static TaskTriggerInfo Every(TimeSpan span) =>
        new() { Type = TaskTriggerInfoType.IntervalTrigger, IntervalTicks = span.Ticks };

    [Fact]
    public void Daily_LaterToday_IsToday()
    {
        Assert.Equal(At(5, 1, 18), NextRunCalculator.Next([Daily(18)], At(5, 1, 9), null, Utc));
    }

    [Fact]
    public void Daily_AlreadyPassedToday_IsTomorrow()
    {
        Assert.Equal(At(5, 2, 8), NextRunCalculator.Next([Daily(8)], At(5, 1, 9), null, Utc));
    }

    [Fact]
    public void Daily_ExactlyNow_IsTomorrow_BecauseItAlreadyStarted()
    {
        Assert.Equal(At(5, 2, 8), NextRunCalculator.Next([Daily(8)], At(5, 1, 8), null, Utc));
    }

    [Fact]
    public void Weekly_PicksTheNextMatchingDay()
    {
        // 2026-05-01 is a Friday.
        Assert.Equal(At(5, 4, 7), NextRunCalculator.Next([Weekly(DayOfWeek.Monday, 7)], At(5, 1, 9), null, Utc));
        Assert.Equal(At(5, 8, 7), NextRunCalculator.Next([Weekly(DayOfWeek.Friday, 7)], At(5, 1, 9), null, Utc));
        Assert.Equal(At(5, 1, 20), NextRunCalculator.Next([Weekly(DayOfWeek.Friday, 20)], At(5, 1, 9), null, Utc));
    }

    [Fact]
    public void Interval_IsCountedFromTheLastRunEnd()
    {
        Assert.Equal(At(5, 1, 12), NextRunCalculator.Next([Every(TimeSpan.FromHours(6))], At(5, 1, 9), At(5, 1, 6), Utc));
    }

    [Fact]
    public void Interval_Overdue_IsNow()
    {
        Assert.Equal(At(5, 1, 9), NextRunCalculator.Next([Every(TimeSpan.FromHours(1))], At(5, 1, 9), At(5, 1, 1), Utc));
    }

    [Fact]
    public void Interval_WithNoLastRun_IsUnknown()
    {
        Assert.Null(NextRunCalculator.Next([Every(TimeSpan.FromHours(1))], At(5, 1, 9), null, Utc));
    }

    [Fact]
    public void StartupTrigger_HasNoTime_AndIsIgnored()
    {
        Assert.Null(NextRunCalculator.Next([new TaskTriggerInfo { Type = TaskTriggerInfoType.StartupTrigger }], At(5, 1, 9), null, Utc));
    }

    [Fact]
    public void SeveralTriggers_GiveTheEarliest()
    {
        var next = NextRunCalculator.Next([Daily(23), Weekly(DayOfWeek.Friday, 12), Every(TimeSpan.FromDays(3))], At(5, 1, 9), At(5, 1, 8), Utc);

        Assert.Equal(At(5, 1, 12), next);
    }

    [Fact]
    public void NoTriggers_IsUnknown()
    {
        Assert.Null(NextRunCalculator.Next(new List<TaskTriggerInfo>(), At(5, 1, 9), null, Utc));
    }

    [Theory]
    [InlineData(-1L)]
    [InlineData(864000000000L)]
    public void Daily_WithAnImpossibleTimeOfDay_IsUnknown(long ticks)
    {
        Assert.Null(NextRunCalculator.Next([new TaskTriggerInfo { Type = TaskTriggerInfoType.DailyTrigger, TimeOfDayTicks = ticks }], At(5, 1, 9), null, Utc));
    }

    [Fact]
    public void TriggersMissingTheirFields_AreUnknown_NotAnError()
    {
        Assert.Null(NextRunCalculator.Next(
            [
                new TaskTriggerInfo { Type = TaskTriggerInfoType.DailyTrigger },
                new TaskTriggerInfo { Type = TaskTriggerInfoType.WeeklyTrigger, TimeOfDayTicks = 0 },
                new TaskTriggerInfo { Type = TaskTriggerInfoType.IntervalTrigger, IntervalTicks = 0 }
            ],
            At(5, 1, 9),
            At(5, 1, 8),
            Utc));
    }

    [Fact]
    public void LocalZone_TimeOfDayIsInThatZone_NotUtc()
    {
        var madrid = FindMadrid();
        if (madrid is null)
        {
            return;
        }

        // 08:00 in Madrid in May (UTC+2) is 06:00 UTC.
        Assert.Equal(At(5, 2, 6), NextRunCalculator.Next([Daily(8)], At(5, 1, 9), null, madrid));
    }

    [Fact]
    public void LocalZone_SkippedSpringForwardHour_RunsAnHourLater()
    {
        var madrid = FindMadrid();
        if (madrid is null)
        {
            return;
        }

        // 2026-03-29 02:30 does not exist in Madrid (02:00 jumps to 03:00).
        var next = NextRunCalculator.Next([Daily(2, 30)], At(3, 28, 12), null, madrid);

        Assert.Equal(At(3, 29, 1, 30), next);
    }

    [Fact]
    public void ScheduleInfo_UsesTheDigestTaskOnly_AndItsLastRunEnd()
    {
        var other = Worker("SomethingElse", [Daily(1)], null);
        var ours = Worker(SendEmailDigestTask.TaskKey, [Every(TimeSpan.FromHours(6))], new TaskResult { EndTimeUtc = new DateTime(2026, 5, 1, 6, 0, 0, DateTimeKind.Utc) });
        var manager = new Mock<ITaskManager>();
        manager.SetupGet(m => m.ScheduledTasks).Returns([other.Object, ours.Object]);
        var info = new JellyfinScheduleInfo(manager.Object, new RunnerHarness.ManualTime(At(5, 1, 9)));

        Assert.Equal(At(5, 1, 12), info.GetNextRunUtc());
    }

    [Fact]
    public void ScheduleInfo_TaskMissing_IsUnknown()
    {
        var manager = new Mock<ITaskManager>();
        manager.SetupGet(m => m.ScheduledTasks).Returns(Array.Empty<IScheduledTaskWorker>());

        Assert.Null(new JellyfinScheduleInfo(manager.Object, TimeProvider.System).GetNextRunUtc());
    }

    [Fact]
    public void ScheduleInfo_NeverRun_IntervalIsUnknown()
    {
        var ours = Worker(SendEmailDigestTask.TaskKey, [Every(TimeSpan.FromHours(6))], null);
        var manager = new Mock<ITaskManager>();
        manager.SetupGet(m => m.ScheduledTasks).Returns([ours.Object]);

        Assert.Null(new JellyfinScheduleInfo(manager.Object, TimeProvider.System).GetNextRunUtc());
    }

    private static TimeZoneInfo? FindMadrid()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Europe/Madrid");
        }
        catch (TimeZoneNotFoundException)
        {
            return null;
        }
    }

    private static Mock<IScheduledTaskWorker> Worker(string key, TaskTriggerInfo[] triggers, TaskResult? last)
    {
        var task = new Mock<IScheduledTask>();
        task.SetupGet(t => t.Key).Returns(key);
        var worker = new Mock<IScheduledTaskWorker>();
        worker.SetupGet(w => w.ScheduledTask).Returns(task.Object);
        worker.SetupGet(w => w.Triggers).Returns(triggers);
        worker.SetupGet(w => w.LastExecutionResult).Returns(last!);
        return worker;
    }
}
