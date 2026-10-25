using System;
using Jellyfin.Plugin.EmailDigest.Delivery;
using Xunit;

namespace Jellyfin.Plugin.EmailDigest.Tests.Delivery;

public sealed class DeliveryWindowPlannerTests
{
    private static readonly DateTimeOffset To = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private static UserDeliveryState State(DateTimeOffset checkpoint)
    {
        return new UserDeliveryState { UserId = Guid.NewGuid(), CheckpointUtc = checkpoint };
    }

    [Fact]
    public void ExistingUser_ContinuesFromTheirOwnCheckpoint_ToTheFixedUpperBound()
    {
        var window = DeliveryWindowPlanner.Plan(State(To.AddHours(-6)), null, 168, To);

        Assert.Equal(To.AddHours(-6), window.FromUtc);
        Assert.Equal(To, window.ToUtc);
        Assert.Equal(DeliveryWindowSource.Checkpoint, window.Source);
        Assert.False(window.IsEmpty);
    }

    [Fact]
    public void ExistingUser_IgnoresTheLegacyCheckpointAndTheLookback()
    {
        var window = DeliveryWindowPlanner.Plan(State(To.AddHours(-2)), To.AddDays(-20), 700, To);

        Assert.Equal(To.AddHours(-2), window.FromUtc);
    }

    [Fact]
    public void NewUser_GetsOnlyTheBoundedLookback_NotTheWholeLibrary()
    {
        var window = DeliveryWindowPlanner.Plan(null, null, 168, To);

        Assert.Equal(To.AddHours(-168), window.FromUtc);
        Assert.Equal(DeliveryWindowSource.NewUserBaseline, window.Source);
    }

    [Fact]
    public void NewUser_WithZeroLookback_StartsFromNowSoNothingIsSent()
    {
        var window = DeliveryWindowPlanner.Plan(null, null, 0, To);

        Assert.True(window.IsEmpty);
        Assert.Equal(To, window.FromUtc);
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(100000, 720)]
    public void NewUser_LookbackIsClampedToTheCatchUpCap(int configured, int effectiveHours)
    {
        var window = DeliveryWindowPlanner.Plan(null, null, configured, To);

        Assert.Equal(To.AddHours(-effectiveHours), window.FromUtc);
    }

    [Fact]
    public void NewUser_WithALegacyCheckpoint_ContinuesFromIt()
    {
        var window = DeliveryWindowPlanner.Plan(null, To.AddDays(-3), 0, To);

        Assert.Equal(To.AddDays(-3), window.FromUtc);
        Assert.Equal(DeliveryWindowSource.LegacyCheckpoint, window.Source);
    }

    [Fact]
    public void LongPausedUser_IsCaughtUpOnlyThirtyDays()
    {
        var window = DeliveryWindowPlanner.Plan(State(To.AddDays(-200)), null, 168, To);

        Assert.Equal(To.AddDays(-30), window.FromUtc);
        Assert.Equal(DeliveryWindowSource.CatchUpLimit, window.Source);
    }

    [Fact]
    public void PausedUserWithinTheLimit_KeepsTheirFullPendingHistory()
    {
        var window = DeliveryWindowPlanner.Plan(State(To.AddDays(-29)), null, 168, To);

        Assert.Equal(To.AddDays(-29), window.FromUtc);
        Assert.Equal(DeliveryWindowSource.Checkpoint, window.Source);
    }

    [Fact]
    public void CheckpointInTheFuture_GivesAnEmptyWindowAndNeverMovesBackwards()
    {
        var window = DeliveryWindowPlanner.Plan(State(To.AddHours(3)), null, 168, To);

        Assert.True(window.IsEmpty);
        Assert.Equal(To.AddHours(3), window.FromUtc);
    }

    [Fact]
    public void CheckpointEqualToTheUpperBound_IsEmpty_ExclusiveStartInclusiveEnd()
    {
        Assert.True(DeliveryWindowPlanner.Plan(State(To), null, 168, To).IsEmpty);
    }

    [Fact]
    public void Plan_IsDeterministic()
    {
        var state = State(To.AddHours(-9));

        Assert.Equal(DeliveryWindowPlanner.Plan(state, null, 24, To), DeliveryWindowPlanner.Plan(state, null, 24, To));
    }
}
