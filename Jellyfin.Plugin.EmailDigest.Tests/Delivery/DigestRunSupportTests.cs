using System;
using Jellyfin.Plugin.EmailDigest.Delivery;
using Jellyfin.Plugin.EmailDigest.Email;
using Xunit;

namespace Jellyfin.Plugin.EmailDigest.Tests.Delivery;

public sealed class DigestRunSupportTests : IDisposable
{
    private readonly string _folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "emaildigest-lock-" + Guid.NewGuid().ToString("N"));

    public DigestRunSupportTests()
    {
        System.IO.Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        System.IO.Directory.Delete(_folder, recursive: true);
    }

    [Theory]
    [InlineData(EmailTransportOutcome.Accepted, null, DeliveryFailureKind.Accepted)]
    [InlineData(EmailTransportOutcome.ConnectionFailed, null, DeliveryFailureKind.Transient)]
    [InlineData(EmailTransportOutcome.Timeout, null, DeliveryFailureKind.Transient)]
    [InlineData(EmailTransportOutcome.RecipientRejected, 550, DeliveryFailureKind.PermanentForUser)]
    [InlineData(EmailTransportOutcome.RecipientRejected, 451, DeliveryFailureKind.Transient)]
    [InlineData(EmailTransportOutcome.RecipientRejected, null, DeliveryFailureKind.PermanentForUser)]
    [InlineData(EmailTransportOutcome.SenderRejected, 553, DeliveryFailureKind.StopRun)]
    [InlineData(EmailTransportOutcome.SenderRejected, 450, DeliveryFailureKind.Transient)]
    [InlineData(EmailTransportOutcome.AuthenticationFailed, null, DeliveryFailureKind.StopRun)]
    [InlineData(EmailTransportOutcome.TlsFailed, null, DeliveryFailureKind.StopRun)]
    [InlineData(EmailTransportOutcome.Uncertain, null, DeliveryFailureKind.Uncertain)]
    [InlineData(EmailTransportOutcome.Cancelled, null, DeliveryFailureKind.Cancelled)]
    [InlineData(EmailTransportOutcome.InvalidRequest, null, DeliveryFailureKind.PermanentForUser)]
    [InlineData(EmailTransportOutcome.UnexpectedError, null, DeliveryFailureKind.PermanentForUser)]
    public void Classifier_MapsEveryOutcome(EmailTransportOutcome outcome, int? code, DeliveryFailureKind expected)
    {
        Assert.Equal(expected, DeliveryFailureClassifier.Classify(new EmailTransportResult(outcome, code)));
    }

    [Fact]
    public void Classifier_CoversEveryTransportOutcome()
    {
        foreach (var outcome in Enum.GetValues<EmailTransportOutcome>())
        {
            Assert.True(Enum.IsDefined(DeliveryFailureClassifier.Classify(new EmailTransportResult(outcome))));
        }
    }

    [Fact]
    public void RetryPolicy_UsesTheBackoffAndRepeatsTheLastValue()
    {
        var policy = new DigestRetryPolicy(4, new[] { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5) });

        Assert.Equal(TimeSpan.FromSeconds(1), policy.DelayBefore(1));
        Assert.Equal(TimeSpan.FromSeconds(5), policy.DelayBefore(2));
        Assert.Equal(TimeSpan.FromSeconds(5), policy.DelayBefore(9));
        Assert.Equal(TimeSpan.Zero, new DigestRetryPolicy(2, Array.Empty<TimeSpan>()).DelayBefore(1));
        Assert.Equal(3, DigestRetryPolicy.Default.MaxAttempts);
    }

    [Fact]
    public void Lock_SecondAcquireFailsWhileHeld_AndWorksAgainAfterRelease()
    {
        var runLock = new FileDigestRunLock(_folder);

        using (var first = runLock.TryAcquire())
        {
            Assert.NotNull(first);
            Assert.Null(runLock.TryAcquire());
        }

        using var again = runLock.TryAcquire();
        Assert.NotNull(again);
    }

    [Fact]
    public void Lock_ADifferentInstanceOnTheSameFolder_IsBlockedToo()
    {
        using var held = new FileDigestRunLock(_folder).TryAcquire();

        Assert.NotNull(held);
        Assert.Null(new FileDigestRunLock(_folder).TryAcquire());
    }

    [Fact]
    public void Lock_DisposingTwice_IsHarmlessAndDoesNotReleaseSomeoneElsesLock()
    {
        var runLock = new FileDigestRunLock(_folder);
        var first = runLock.TryAcquire();
        first!.Dispose();
        using var second = runLock.TryAcquire();

        first.Dispose();

        Assert.NotNull(second);
        Assert.Null(runLock.TryAcquire());
    }

    [Fact]
    public void Lock_AStaleFileOnDisk_DoesNotBlock()
    {
        System.IO.File.WriteAllText(System.IO.Path.Combine(_folder, "digest-run.lock"), "stale");

        using var handle = new FileDigestRunLock(_folder).TryAcquire();

        Assert.NotNull(handle);
    }
}
