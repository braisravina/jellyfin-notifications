using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.EmailDigest.Delivery;
using Xunit;

namespace Jellyfin.Plugin.EmailDigest.Tests.Delivery;

public sealed class DeliveryStatusStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "EmailDigestStatus_" + Guid.NewGuid());
    private readonly DeliveryStatusStore _store;

    public DeliveryStatusStoreTests()
    {
        Directory.CreateDirectory(_folder);
        _store = new DeliveryStatusStore(_folder);
    }

    private string StatePath => Path.Combine(_folder, "delivery-status.json");

    public void Dispose()
    {
        _store.Dispose();
        Directory.Delete(_folder, recursive: true);
    }

    private static DateTimeOffset T(int hour) => new(2026, 5, 1, hour, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task UnknownUser_HasNoStatus()
    {
        Assert.Null(await _store.GetAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task Accepted_SetsBothTheAttemptAndTheLastAccepted()
    {
        var user = Guid.NewGuid();

        await _store.RecordAttemptAsync(user, DeliveryAttemptStatus.Accepted, T(9), CancellationToken.None);

        var status = await _store.GetAsync(user, CancellationToken.None);
        Assert.Equal(T(9), status!.LastAcceptedUtc);
        Assert.Equal(T(9), status.LastAttemptUtc);
        Assert.Equal(DeliveryAttemptStatus.Accepted, status.LastAttemptStatus);
    }

    [Theory]
    [InlineData(DeliveryAttemptStatus.Failed)]
    [InlineData(DeliveryAttemptStatus.Uncertain)]
    public async Task FailedAttempt_KeepsThePreviousLastAccepted(DeliveryAttemptStatus status)
    {
        var user = Guid.NewGuid();
        await _store.RecordAttemptAsync(user, DeliveryAttemptStatus.Accepted, T(9), CancellationToken.None);

        await _store.RecordAttemptAsync(user, status, T(15), CancellationToken.None);

        var result = await _store.GetAsync(user, CancellationToken.None);
        Assert.Equal(T(9), result!.LastAcceptedUtc);
        Assert.Equal(T(15), result.LastAttemptUtc);
        Assert.Equal(status, result.LastAttemptStatus);
    }

    [Fact]
    public async Task FailureForAUserWhoNeverSucceeded_HasNoLastAccepted()
    {
        var user = Guid.NewGuid();

        await _store.RecordAttemptAsync(user, DeliveryAttemptStatus.Failed, T(9), CancellationToken.None);

        Assert.Null((await _store.GetAsync(user, CancellationToken.None))!.LastAcceptedUtc);
    }

    [Fact]
    public async Task RecordingForOneUser_NeverChangesAnother()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await _store.RecordAttemptAsync(a, DeliveryAttemptStatus.Accepted, T(9), CancellationToken.None);
        var before = await _store.GetAsync(a, CancellationToken.None);

        await _store.RecordAttemptAsync(b, DeliveryAttemptStatus.Failed, T(10), CancellationToken.None);

        var after = await _store.GetAsync(a, CancellationToken.None);
        Assert.Equal(before!.LastAttemptUtc, after!.LastAttemptUtc);
        Assert.Equal(DeliveryAttemptStatus.Accepted, after.LastAttemptStatus);
    }

    [Fact]
    public async Task ConcurrentRecords_ForDifferentUsers_AreAllKept()
    {
        var ids = Enumerable.Range(0, 25).Select(_ => Guid.NewGuid()).ToList();

        await Task.WhenAll(ids.Select(id => _store.RecordAttemptAsync(id, DeliveryAttemptStatus.Accepted, T(9), CancellationToken.None)));

        foreach (var id in ids)
        {
            Assert.NotNull(await _store.GetAsync(id, CancellationToken.None));
        }
    }

    [Fact]
    public async Task DamagedFile_IsMovedAside_AndTheStoreStartsEmpty()
    {
        await File.WriteAllTextAsync(StatePath, "{ nope");

        Assert.Null(await _store.GetAsync(Guid.NewGuid(), CancellationToken.None));
        await _store.RecordAttemptAsync(Guid.NewGuid(), DeliveryAttemptStatus.Accepted, T(9), CancellationToken.None);

        Assert.Single(Directory.GetFiles(_folder, "delivery-status.json.damaged-*"));
    }

    [Fact]
    public async Task StoredFile_ContainsNoAddressOrErrorText()
    {
        await _store.RecordAttemptAsync(Guid.NewGuid(), DeliveryAttemptStatus.Failed, T(9), CancellationToken.None);

        var text = await File.ReadAllTextAsync(StatePath);

        Assert.DoesNotContain("@", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EmptyUserId_IsRejected()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _store.RecordAttemptAsync(Guid.Empty, DeliveryAttemptStatus.Accepted, T(9), CancellationToken.None));
    }
}
