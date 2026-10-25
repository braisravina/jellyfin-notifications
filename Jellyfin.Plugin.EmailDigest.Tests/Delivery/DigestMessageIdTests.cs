using System;
using System.Linq;
using Jellyfin.Plugin.EmailDigest.Delivery;
using Xunit;

namespace Jellyfin.Plugin.EmailDigest.Tests.Delivery;

public sealed class DigestMessageIdTests
{
    private static readonly Guid User = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset To = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly DeliveryWindow Window = new(To.AddHours(-24), To, DeliveryWindowSource.Checkpoint);

    [Fact]
    public void SameDigest_SameId_RegardlessOfItemOrder()
    {
        var ids = Enumerable.Range(1, 5).Select(_ => Guid.NewGuid()).ToList();

        var first = DigestMessageId.Create(User, Window, ids);
        var second = DigestMessageId.Create(User, Window, ids.AsEnumerable().Reverse());

        Assert.Equal(first, second);
    }

    [Fact]
    public void AnyDifference_ChangesTheId()
    {
        var item = Guid.NewGuid();
        var baseline = DigestMessageId.Create(User, Window, new[] { item });

        Assert.NotEqual(baseline, DigestMessageId.Create(Guid.NewGuid(), Window, new[] { item }));
        Assert.NotEqual(baseline, DigestMessageId.Create(User, Window with { ToUtc = To.AddTicks(1) }, new[] { item }));
        Assert.NotEqual(baseline, DigestMessageId.Create(User, Window with { FromUtc = Window.FromUtc.AddTicks(1) }, new[] { item }));
        Assert.NotEqual(baseline, DigestMessageId.Create(User, Window, new[] { item, Guid.NewGuid() }));
    }

    [Fact]
    public void Id_IsAValidMessageIdPartAndRevealsNothing()
    {
        var item = Guid.NewGuid();

        var id = DigestMessageId.Create(User, Window, new[] { item });

        Assert.Equal(44, id.Length);
        Assert.StartsWith("emaildigest-", id, StringComparison.Ordinal);
        Assert.All(id, c => Assert.True(char.IsAsciiLetterOrDigit(c) || c == '-'));
        Assert.DoesNotContain(User.ToString("N"), id, StringComparison.Ordinal);
        Assert.DoesNotContain(item.ToString("N"), id, StringComparison.Ordinal);
    }

    [Fact]
    public void NullItems_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => DigestMessageId.Create(User, Window, null!));
    }
}
