using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.EmailDigest.Delivery;
using Xunit;

namespace Jellyfin.Plugin.EmailDigest.Tests.Delivery;

public sealed class DeliveryStateStoreTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "emaildigest-state-" + Guid.NewGuid().ToString("N"));
    private readonly ManualTime _time = new(Now);

    public DeliveryStateStoreTests()
    {
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }

    private DeliveryStateStore Store()
    {
        return new DeliveryStateStore(_directory, _time);
    }

    private string StateFile => Path.Combine(_directory, "delivery-state.json");

    private static UserDeliveryState State(Guid? id = null, DateTimeOffset? checkpoint = null)
    {
        return new UserDeliveryState { UserId = id ?? Guid.NewGuid(), CheckpointUtc = checkpoint ?? Now.AddHours(-1) };
    }

    [Fact]
    public async Task Get_Fresh_IsNullAndEmpty()
    {
        using var store = Store();

        Assert.Null(await store.GetAsync(Guid.NewGuid(), CancellationToken.None));
        Assert.Empty(await store.GetAllAsync(CancellationToken.None));
        Assert.False(File.Exists(StateFile));
    }

    [Fact]
    public async Task Save_RoundTripsEveryField()
    {
        using var store = Store();
        var state = State();
        state.LastAttemptId = "attempt-1";
        state.LastOutcome = "Accepted";
        state.Delivered.Add(new DeliveredItem { ItemId = Guid.NewGuid(), DeliveredUtc = Now.AddDays(-1) });

        await store.SaveAsync(state, CancellationToken.None);
        var loaded = await Store().GetAsync(state.UserId, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(state.CheckpointUtc, loaded!.CheckpointUtc);
        Assert.Equal("attempt-1", loaded.LastAttemptId);
        Assert.Equal("Accepted", loaded.LastOutcome);
        Assert.Equal(Now, loaded.UpdatedUtc);
        Assert.Equal(state.Delivered[0].ItemId, Assert.Single(loaded.Delivered).ItemId);
    }

    [Fact]
    public async Task Users_AreIndependent_SavingOneNeverChangesAnother()
    {
        using var store = Store();
        var a = State(checkpoint: Now.AddHours(-5));
        var b = State(checkpoint: Now.AddHours(-9));
        await store.SaveAsync(a, CancellationToken.None);
        await store.SaveAsync(b, CancellationToken.None);

        var advanced = State(a.UserId, Now);
        await store.SaveAsync(advanced, CancellationToken.None);

        Assert.Equal(Now, (await store.GetAsync(a.UserId, CancellationToken.None))!.CheckpointUtc);
        Assert.Equal(Now.AddHours(-9), (await store.GetAsync(b.UserId, CancellationToken.None))!.CheckpointUtc);
        Assert.Equal(2, (await store.GetAllAsync(CancellationToken.None)).Count);
    }

    [Fact]
    public async Task Save_PrunesDeliveredItemsOlderThanNinetyDaysOnly()
    {
        using var store = Store();
        var state = State();
        var old = new DeliveredItem { ItemId = Guid.NewGuid(), DeliveredUtc = Now.AddDays(-91) };
        var recent = new DeliveredItem { ItemId = Guid.NewGuid(), DeliveredUtc = Now.AddDays(-89) };
        state.Delivered.Add(old);
        state.Delivered.Add(recent);

        await store.SaveAsync(state, CancellationToken.None);

        var loaded = await store.GetAsync(state.UserId, CancellationToken.None);
        Assert.Equal(recent.ItemId, Assert.Single(loaded!.Delivered).ItemId);
    }

    [Fact]
    public async Task Save_LeavesNoTemporaryFile_AndKeepsTheLegacyFileUntouched()
    {
        var legacy = Path.Combine(_directory, "checkpoint.json");
        await File.WriteAllTextAsync(legacy, "{\"LastSuccessfulRunUtc\":\"2026-05-01T00:00:00+00:00\"}");
        var before = await File.ReadAllTextAsync(legacy);
        using var store = Store();

        await store.SaveAsync(State(), CancellationToken.None);
        await store.SaveAsync(State(), CancellationToken.None);

        Assert.Empty(Directory.GetFiles(_directory, "*.tmp"));
        Assert.Equal(before, await File.ReadAllTextAsync(legacy));
    }

    [Fact]
    public async Task Save_KeepsTheTwoMostRecentGenerations()
    {
        using var store = Store();
        var user = Guid.NewGuid();
        await store.SaveAsync(State(user, Now.AddHours(-3)), CancellationToken.None);
        await store.SaveAsync(State(user, Now.AddHours(-2)), CancellationToken.None);

        var backup = await File.ReadAllTextAsync(StateFile + ".bak");

        Assert.Contains(Now.AddHours(-3).ToString("yyyy-MM-dd'T'HH:mm", System.Globalization.CultureInfo.InvariantCulture), backup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DamagedFile_RaisesAndIsNeverOverwrittenOrTreatedAsEmpty()
    {
        await File.WriteAllTextAsync(StateFile, "{ this is not json");
        using var store = Store();

        await Assert.ThrowsAsync<DeliveryStateException>(() => store.GetAsync(Guid.NewGuid(), CancellationToken.None));
        await Assert.ThrowsAsync<DeliveryStateException>(() => store.SaveAsync(State(), CancellationToken.None));

        Assert.Equal("{ this is not json", await File.ReadAllTextAsync(StateFile));
        Assert.False(File.Exists(StateFile + ".bak"));
    }

    [Theory]
    [InlineData("{\"Format\":2,\"Users\":[]}")]
    [InlineData("{\"Format\":0,\"Users\":[]}")]
    [InlineData("{\"Format\":1}")]
    [InlineData("{\"Format\":1,\"Users\":[{\"UserId\":\"00000000-0000-0000-0000-000000000000\"}]}")]
    [InlineData("{\"Format\":1,\"Users\":[null]}")]
    public async Task UnsupportedOrInvalidContent_RaisesAndIsLeftUntouched(string content)
    {
        await File.WriteAllTextAsync(StateFile, content);
        using var store = Store();

        await Assert.ThrowsAsync<DeliveryStateException>(() => store.GetAllAsync(CancellationToken.None));
        await Assert.ThrowsAsync<DeliveryStateException>(() => store.SaveAsync(State(), CancellationToken.None));

        Assert.Equal(content, await File.ReadAllTextAsync(StateFile));
    }

    [Fact]
    public async Task Save_WithoutAUserId_IsRejected()
    {
        using var store = Store();

        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(new UserDeliveryState(), CancellationToken.None));
    }

    [Fact]
    public async Task ParallelSavesForDifferentUsers_AreAllPersisted()
    {
        using var store = Store();
        var users = Enumerable.Range(0, 25).Select(_ => Guid.NewGuid()).ToList();

        await Task.WhenAll(users.Select(u => store.SaveAsync(State(u), CancellationToken.None)));

        var all = await store.GetAllAsync(CancellationToken.None);
        Assert.Equal(users.OrderBy(u => u), all.Select(s => s.UserId).OrderBy(u => u));
    }

    [Fact]
    public async Task Save_WhenTheWriteFails_ThePreviousStateIsIntact()
    {
        using var store = Store();
        var user = Guid.NewGuid();
        await store.SaveAsync(State(user, Now.AddHours(-4)), CancellationToken.None);
        // A directory squatting on the temp file name makes the next write fail.
        Directory.CreateDirectory(StateFile + ".tmp");

        await Assert.ThrowsAnyAsync<Exception>(() => store.SaveAsync(State(user, Now), CancellationToken.None));

        Assert.Equal(Now.AddHours(-4), (await Store().GetAsync(user, CancellationToken.None))!.CheckpointUtc);
    }

    [Fact]
    public async Task Save_CancelledToken_WritesNothing()
    {
        using var store = Store();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.SaveAsync(State(), cts.Token));

        Assert.False(File.Exists(StateFile));
    }

    // Legacy checkpoint

    [Fact]
    public async Task LegacyCheckpoint_Missing_IsNull()
    {
        using var store = Store();

        Assert.Null(await store.GetLegacyCheckpointAsync(CancellationToken.None));
    }

    [Fact]
    public async Task LegacyCheckpoint_Present_IsReadWithoutBeingChanged()
    {
        var legacy = Path.Combine(_directory, "checkpoint.json");
        await File.WriteAllTextAsync(legacy, "{\"LastSuccessfulRunUtc\":\"2026-05-01T00:00:00+00:00\"}");
        using var store = Store();

        var value = await store.GetLegacyCheckpointAsync(CancellationToken.None);

        Assert.Equal(new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero), value);
        Assert.True(File.Exists(legacy));
        Assert.Equal(new[] { legacy }, Directory.GetFiles(_directory));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("{\"LastSuccessfulRunUtc\":\"0001-01-01T00:00:00+00:00\"}")]
    public async Task LegacyCheckpoint_DamagedOrEmpty_IsIgnoredNotFatal(string content)
    {
        await File.WriteAllTextAsync(Path.Combine(_directory, "checkpoint.json"), content);
        using var store = Store();

        Assert.Null(await store.GetLegacyCheckpointAsync(CancellationToken.None));
    }

    private sealed class ManualTime : TimeProvider
    {
        private readonly DateTimeOffset _now;

        public ManualTime(DateTimeOffset now)
        {
            _now = now;
        }

        public override DateTimeOffset GetUtcNow()
        {
            return _now;
        }
    }
}
