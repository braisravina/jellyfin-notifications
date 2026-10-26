using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.EmailDigest.Tests.Delivery;
using Jellyfin.Plugin.EmailDigest.Verification;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.EmailDigest.Tests.Verification;

public sealed class AddressVerificationServiceTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "EmailDigestVerify_" + Guid.NewGuid());
    private readonly RunnerHarness.ManualTime _time = new(new DateTimeOffset(2026, 5, 1, 12, 0, 0, TimeSpan.Zero));
    private readonly AddressVerificationService _service;
    private VerificationPolicy _policy = VerificationPolicy.Default;

    public AddressVerificationServiceTests()
    {
        Directory.CreateDirectory(_folder);
        _service = new AddressVerificationService(_folder, () => _policy, _time, NullLogger.Instance);
    }

    private string StatePath => Path.Combine(_folder, "verification-requests.json");

    public void Dispose()
    {
        _service.Dispose();
        Directory.Delete(_folder, recursive: true);
    }

    private static Task<bool> Accept(ConfirmedAddress address, CancellationToken token) => Task.FromResult(true);

    // Token creation

    [Fact]
    public async Task Issue_ReturnsAnUnguessableUrlSafeToken_EveryTimeDifferent()
    {
        var a = await _service.IssueAsync(Guid.NewGuid(), "a@example.com", CancellationToken.None);
        var b = await _service.IssueAsync(Guid.NewGuid(), "b@example.com", CancellationToken.None);

        Assert.True(a.Issued);
        Assert.Equal(43, a.Token!.Length);
        Assert.All(a.Token, c => Assert.True(char.IsAsciiLetterOrDigit(c) || c is '-' or '_'));
        Assert.NotEqual(a.Token, b.Token);
    }

    [Fact]
    public async Task Token_IsNeverStored_OnlyItsHash()
    {
        var result = await _service.IssueAsync(Guid.NewGuid(), "a@example.com", CancellationToken.None);

        var stored = await File.ReadAllTextAsync(StatePath);

        Assert.DoesNotContain(result.Token!, stored, StringComparison.Ordinal);
        Assert.Contains("TokenHash", stored, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Issue_ReportsTheConfiguredExpiry()
    {
        _policy = VerificationPolicy.ForLifetime(15);

        var result = await _service.IssueAsync(Guid.NewGuid(), "a@example.com", CancellationToken.None);

        Assert.Equal(_time.GetUtcNow().AddMinutes(15), result.ExpiresUtc);
    }

    [Theory]
    [InlineData(1, 5)]
    [InlineData(4000, 1440)]
    [InlineData(90, 90)]
    public void ForLifetime_ClampsToTheAcceptedRange(int asked, int expectedMinutes)
    {
        Assert.Equal(TimeSpan.FromMinutes(expectedMinutes), VerificationPolicy.ForLifetime(asked).Lifetime);
    }

    // Invalid addresses

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-address")]
    [InlineData("a@example.com, b@example.com")]
    [InlineData("Ana <a@example.com>")]
    [InlineData("a@example.com\r\nBcc: victim@example.com")]
    [InlineData("a b@example.com")]
    public async Task Issue_InvalidAddress_IsRefusedAndNothingIsStored(string? address)
    {
        var result = await _service.IssueAsync(Guid.NewGuid(), address, CancellationToken.None);

        Assert.False(result.Issued);
        Assert.True(result.InvalidAddress);
        Assert.False(File.Exists(StatePath));
    }

    [Fact]
    public async Task Issue_TooLongAddress_IsRefused()
    {
        var result = await _service.IssueAsync(Guid.NewGuid(), new string('a', 250) + "@example.com", CancellationToken.None);

        Assert.True(result.InvalidAddress);
    }

    [Fact]
    public async Task Issue_TrimsSurroundingSpaces()
    {
        var result = await _service.IssueAsync(Guid.NewGuid(), "  a@example.com  ", CancellationToken.None);

        Assert.Equal("a@example.com", result.Address);
    }

    // Confirmation, single use, replay

    [Fact]
    public async Task Confirm_ValidToken_AppliesTheAddressForThatUserOnly()
    {
        var user = Guid.NewGuid();
        var issued = await _service.IssueAsync(user, "a@example.com", CancellationToken.None);
        ConfirmedAddress? seen = null;

        var outcome = await _service.ConfirmAsync(issued.Token, (c, _) => { seen = c; return Task.FromResult(true); }, CancellationToken.None);

        Assert.Equal(ConfirmOutcome.Confirmed, outcome);
        Assert.Equal(new ConfirmedAddress(user, "a@example.com"), seen);
    }

    [Fact]
    public async Task Confirm_Replay_IsRefused_AndDoesNotApplyAgain()
    {
        var issued = await _service.IssueAsync(Guid.NewGuid(), "a@example.com", CancellationToken.None);
        var applied = 0;
        Task<bool> Count(ConfirmedAddress c, CancellationToken t)
        {
            applied++;
            return Task.FromResult(true);
        }

        Assert.Equal(ConfirmOutcome.Confirmed, await _service.ConfirmAsync(issued.Token, Count, CancellationToken.None));
        Assert.Equal(ConfirmOutcome.Invalid, await _service.ConfirmAsync(issued.Token, Count, CancellationToken.None));
        Assert.Equal(1, applied);
    }

    [Fact]
    public async Task Confirm_TwoSimultaneousRedemptions_OnlyOneSucceeds()
    {
        var issued = await _service.IssueAsync(Guid.NewGuid(), "a@example.com", CancellationToken.None);
        var applied = 0;
        Task<bool> Count(ConfirmedAddress c, CancellationToken t)
        {
            Interlocked.Increment(ref applied);
            return Task.FromResult(true);
        }

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => _service.ConfirmAsync(issued.Token, Count, CancellationToken.None)));

        Assert.Equal(1, outcomes.Count(o => o == ConfirmOutcome.Confirmed));
        Assert.Equal(11, outcomes.Count(o => o == ConfirmOutcome.Invalid));
        Assert.Equal(1, applied);
    }

    [Fact]
    public async Task Confirm_ApplyFails_TokenStaysUsedUp()
    {
        var issued = await _service.IssueAsync(Guid.NewGuid(), "a@example.com", CancellationToken.None);

        Assert.Equal(ConfirmOutcome.NotApplied, await _service.ConfirmAsync(issued.Token, (_, _) => Task.FromResult(false), CancellationToken.None));
        Assert.Equal(ConfirmOutcome.Invalid, await _service.ConfirmAsync(issued.Token, Accept, CancellationToken.None));
    }

    [Fact]
    public async Task Confirm_ApplyThrows_TokenStaysUsedUp()
    {
        var issued = await _service.IssueAsync(Guid.NewGuid(), "a@example.com", CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.ConfirmAsync(issued.Token, (_, _) => throw new InvalidOperationException(), CancellationToken.None));
        Assert.Equal(ConfirmOutcome.Invalid, await _service.ConfirmAsync(issued.Token, Accept, CancellationToken.None));
    }

    // Expiry, replacement, cancellation, outdated

    [Fact]
    public async Task Confirm_ExpiredToken_IsRefused()
    {
        var issued = await _service.IssueAsync(Guid.NewGuid(), "a@example.com", CancellationToken.None);
        _time.Advance(_policy.Lifetime);

        Assert.Equal(ConfirmOutcome.Invalid, await _service.ConfirmAsync(issued.Token, Accept, CancellationToken.None));
    }

    [Fact]
    public async Task Confirm_JustBeforeExpiry_Works()
    {
        var issued = await _service.IssueAsync(Guid.NewGuid(), "a@example.com", CancellationToken.None);
        _time.Advance(_policy.Lifetime - TimeSpan.FromSeconds(1));

        Assert.Equal(ConfirmOutcome.Confirmed, await _service.ConfirmAsync(issued.Token, Accept, CancellationToken.None));
    }

    [Fact]
    public async Task NewRequest_ReplacesTheOldOne_AndTheOldLinkStopsWorking()
    {
        var user = Guid.NewGuid();
        var first = await _service.IssueAsync(user, "old@example.com", CancellationToken.None);
        _time.Advance(TimeSpan.FromMinutes(2));
        var second = await _service.IssueAsync(user, "new@example.com", CancellationToken.None);

        Assert.Equal(ConfirmOutcome.Invalid, await _service.ConfirmAsync(first.Token, Accept, CancellationToken.None));
        ConfirmedAddress? seen = null;
        Assert.Equal(ConfirmOutcome.Confirmed, await _service.ConfirmAsync(second.Token, (c, _) => { seen = c; return Task.FromResult(true); }, CancellationToken.None));
        Assert.Equal("new@example.com", seen!.Address);
    }

    [Fact]
    public async Task Cancel_InvalidatesTheLink_AndClearsThePendingView()
    {
        var user = Guid.NewGuid();
        var issued = await _service.IssueAsync(user, "a@example.com", CancellationToken.None);
        Assert.NotNull(await _service.GetPendingAsync(user, CancellationToken.None));

        Assert.True(await _service.CancelAsync(user, CancellationToken.None));

        Assert.Null(await _service.GetPendingAsync(user, CancellationToken.None));
        Assert.Equal(ConfirmOutcome.Invalid, await _service.ConfirmAsync(issued.Token, Accept, CancellationToken.None));
        Assert.False(await _service.CancelAsync(user, CancellationToken.None));
    }

    [Fact]
    public async Task Cancel_OnlyAffectsTheCallingUser()
    {
        var mine = Guid.NewGuid();
        var other = Guid.NewGuid();
        await _service.IssueAsync(mine, "a@example.com", CancellationToken.None);
        var theirs = await _service.IssueAsync(other, "b@example.com", CancellationToken.None);

        await _service.CancelAsync(mine, CancellationToken.None);

        Assert.NotNull(await _service.GetPendingAsync(other, CancellationToken.None));
        Assert.Equal(ConfirmOutcome.Confirmed, await _service.ConfirmAsync(theirs.Token, Accept, CancellationToken.None));
    }

    [Fact]
    public async Task GetPending_ShowsOnlyTheUsersOwnRequest_WithoutAnySecret()
    {
        var mine = Guid.NewGuid();
        var issued = await _service.IssueAsync(mine, "a@example.com", CancellationToken.None);
        await _service.IssueAsync(Guid.NewGuid(), "someone-else@example.com", CancellationToken.None);

        var pending = await _service.GetPendingAsync(mine, CancellationToken.None);

        Assert.Equal("a@example.com", pending!.Address);
        Assert.Equal(_time.GetUtcNow() + _policy.Lifetime, pending.ExpiresUtc);
        Assert.DoesNotContain(issued.Token!, pending.ToString(), StringComparison.Ordinal);
        Assert.Null(await _service.GetPendingAsync(Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task GetPending_AfterExpiry_IsNull()
    {
        var user = Guid.NewGuid();
        await _service.IssueAsync(user, "a@example.com", CancellationToken.None);
        _time.Advance(_policy.Lifetime);

        Assert.Null(await _service.GetPendingAsync(user, CancellationToken.None));
    }

    // Incorrect tokens

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("0123456789012345678901234567890123456789012")]
    [InlineData("01234567890123456789012345678901234567890123")]
    [InlineData("0123456789012345678901234567890123456789 12")]
    [InlineData("0123456789012345678901234567890123456789+/=")]
    public async Task Confirm_WrongOrMalformedToken_IsInvalid_AndAppliesNothing(string? token)
    {
        await _service.IssueAsync(Guid.NewGuid(), "a@example.com", CancellationToken.None);
        var applied = false;

        var outcome = await _service.ConfirmAsync(token, (_, _) => { applied = true; return Task.FromResult(true); }, CancellationToken.None);

        Assert.Equal(ConfirmOutcome.Invalid, outcome);
        Assert.False(applied);
    }

    [Fact]
    public async Task Confirm_AlteredToken_IsInvalid_AndTheRealOneStillWorks()
    {
        var issued = await _service.IssueAsync(Guid.NewGuid(), "a@example.com", CancellationToken.None);
        var altered = (issued.Token![0] == 'A' ? 'B' : 'A') + issued.Token[1..];

        Assert.Equal(ConfirmOutcome.Invalid, await _service.ConfirmAsync(altered, Accept, CancellationToken.None));
        Assert.Equal(ConfirmOutcome.Confirmed, await _service.ConfirmAsync(issued.Token, Accept, CancellationToken.None));
    }

    [Fact]
    public async Task Confirm_AnotherUsersAddressRequest_CannotBeUsedToConfirmForSomeoneElse()
    {
        var alice = Guid.NewGuid();
        var bob = Guid.NewGuid();
        var forAlice = await _service.IssueAsync(alice, "alice@example.com", CancellationToken.None);
        await _service.IssueAsync(bob, "bob@example.com", CancellationToken.None);
        ConfirmedAddress? seen = null;

        await _service.ConfirmAsync(forAlice.Token, (c, _) => { seen = c; return Task.FromResult(true); }, CancellationToken.None);

        Assert.Equal(alice, seen!.UserId);
        Assert.NotNull(await _service.GetPendingAsync(bob, CancellationToken.None));
    }

    // Concurrent address changes

    [Fact]
    public async Task ConcurrentRequestsByOneUser_LeaveExactlyOnePendingRequest()
    {
        _policy = _policy with { Cooldown = TimeSpan.Zero, PerUserPerHour = 100, PerUserPerDay = 100 };
        var user = Guid.NewGuid();

        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(i => _service.IssueAsync(user, $"a{i}@example.com", CancellationToken.None)));

        Assert.All(results, r => Assert.True(r.Issued));
        var live = 0;
        foreach (var r in results)
        {
            if (await _service.ConfirmAsync(r.Token, Accept, CancellationToken.None) == ConfirmOutcome.Confirmed)
            {
                live++;
            }
        }

        Assert.Equal(1, live);
    }

    // Rate limiting

    [Fact]
    public async Task Cooldown_BlocksAnImmediateSecondRequest_ThenAllowsItAfterTheWait()
    {
        var user = Guid.NewGuid();
        await _service.IssueAsync(user, "a@example.com", CancellationToken.None);

        var blocked = await _service.IssueAsync(user, "a@example.com", CancellationToken.None);
        Assert.Equal(RateLimitKind.Cooldown, blocked.RateLimit);
        Assert.Equal(TimeSpan.FromMinutes(1), blocked.RetryAfter);
        Assert.Null(blocked.Token);

        _time.Advance(blocked.RetryAfter);
        Assert.True((await _service.IssueAsync(user, "a@example.com", CancellationToken.None)).Issued);
    }

    [Fact]
    public async Task PerUserHourlyLimit_Applies_AndReportsWhenToRetry()
    {
        var user = Guid.NewGuid();
        for (var i = 0; i < 3; i++)
        {
            Assert.True((await _service.IssueAsync(user, $"a{i}@example.com", CancellationToken.None)).Issued);
            _time.Advance(TimeSpan.FromMinutes(2));
        }

        var blocked = await _service.IssueAsync(user, "a9@example.com", CancellationToken.None);

        Assert.Equal(RateLimitKind.User, blocked.RateLimit);
        Assert.Equal(TimeSpan.FromMinutes(54), blocked.RetryAfter);
    }

    [Fact]
    public async Task PerUserDailyLimit_Applies()
    {
        var user = Guid.NewGuid();
        for (var i = 0; i < 10; i++)
        {
            Assert.True((await _service.IssueAsync(user, $"a{i}@example.com", CancellationToken.None)).Issued, $"request {i}");
            _time.Advance(TimeSpan.FromMinutes(25));
        }

        var blocked = await _service.IssueAsync(user, "late@example.com", CancellationToken.None);

        Assert.Equal(RateLimitKind.User, blocked.RateLimit);
    }

    [Fact]
    public async Task PerAddressLimit_StopsManyUsersMailingTheSameInbox_RegardlessOfCase()
    {
        var spellings = new[] { "victim@example.com", "VICTIM@example.com", "Victim@Example.com" };
        foreach (var spelling in spellings)
        {
            Assert.True((await _service.IssueAsync(Guid.NewGuid(), spelling, CancellationToken.None)).Issued);
        }

        var blocked = await _service.IssueAsync(Guid.NewGuid(), "victim@example.com", CancellationToken.None);

        Assert.Equal(RateLimitKind.Address, blocked.RateLimit);
        Assert.True((await _service.IssueAsync(Guid.NewGuid(), "someone-else@example.com", CancellationToken.None)).Issued);
    }

    [Fact]
    public async Task GlobalLimit_BoundsWhatThePluginAsksSmtpToSend()
    {
        _policy = _policy with { GlobalPerHour = 5 };
        for (var i = 0; i < 5; i++)
        {
            Assert.True((await _service.IssueAsync(Guid.NewGuid(), $"u{i}@example.com", CancellationToken.None)).Issued);
        }

        var blocked = await _service.IssueAsync(Guid.NewGuid(), "u9@example.com", CancellationToken.None);

        Assert.Equal(RateLimitKind.Global, blocked.RateLimit);
    }

    [Fact]
    public async Task RateLimits_SurviveARestart()
    {
        var user = Guid.NewGuid();
        await _service.IssueAsync(user, "a@example.com", CancellationToken.None);
        using var restarted = new AddressVerificationService(_folder, () => _policy, _time, NullLogger.Instance);

        var blocked = await restarted.IssueAsync(user, "a@example.com", CancellationToken.None);

        Assert.Equal(RateLimitKind.Cooldown, blocked.RateLimit);
    }

    [Fact]
    public async Task ARefusedRequest_DoesNotCountAgainstTheLimits_NorStoreAnything()
    {
        var user = Guid.NewGuid();
        await _service.IssueAsync(user, "a@example.com", CancellationToken.None);
        var before = await File.ReadAllTextAsync(StatePath);

        await _service.IssueAsync(user, "b@example.com", CancellationToken.None);
        await _service.IssueAsync(user, "not-an-address", CancellationToken.None);

        Assert.Equal(before, await File.ReadAllTextAsync(StatePath));
    }

    [Fact]
    public async Task OldLogEntries_AreDropped_SoStateDoesNotGrowForever()
    {
        var user = Guid.NewGuid();
        await _service.IssueAsync(user, "a@example.com", CancellationToken.None);
        _time.Advance(TimeSpan.FromHours(25));

        await _service.IssueAsync(user, "b@example.com", CancellationToken.None);

        var stored = await File.ReadAllTextAsync(StatePath);
        Assert.Equal(1, stored.Split("AddressKey", StringSplitOptions.None).Length - 1);
    }

    // Damaged state, permissions

    [Fact]
    public async Task DamagedState_IsMovedAside_NotDeleted_AndVerificationStartsEmpty()
    {
        await File.WriteAllTextAsync(StatePath, "{ not json");

        var result = await _service.IssueAsync(Guid.NewGuid(), "a@example.com", CancellationToken.None);

        Assert.True(result.Issued);
        var aside = Directory.GetFiles(_folder, "verification-requests.json.damaged-*");
        Assert.Single(aside);
        Assert.Equal("{ not json", await File.ReadAllTextAsync(aside[0]));
    }

    [Fact]
    public async Task UnsupportedStateFormat_IsMovedAsideToo()
    {
        await File.WriteAllTextAsync(StatePath, "{\"Format\":99,\"Pending\":[],\"Log\":[]}");

        Assert.True((await _service.IssueAsync(Guid.NewGuid(), "a@example.com", CancellationToken.None)).Issued);
        Assert.Single(Directory.GetFiles(_folder, "verification-requests.json.damaged-*"));
    }

    [Fact]
    public async Task StateFile_IsOwnerOnly_OnUnix()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        await _service.IssueAsync(Guid.NewGuid(), "a@example.com", CancellationToken.None);

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(StatePath));
    }

    [Fact]
    public async Task StorageFailure_FailsClosed_NothingIsConfirmedOrIssued()
    {
        var issued = await _service.IssueAsync(Guid.NewGuid(), "a@example.com", CancellationToken.None);
        File.Delete(StatePath);
        Directory.CreateDirectory(StatePath); // the state path now cannot be written as a file
        var applied = false;

        Assert.Equal(ConfirmOutcome.Invalid, await _service.ConfirmAsync(issued.Token, (_, _) => { applied = true; return Task.FromResult(true); }, CancellationToken.None));
        await Assert.ThrowsAsync<VerificationStoreException>(() => _service.IssueAsync(Guid.NewGuid(), "b@example.com", CancellationToken.None));
        Assert.False(applied);
    }

    [Fact]
    public async Task Issue_RejectsAnEmptyUserId()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.IssueAsync(Guid.Empty, "a@example.com", CancellationToken.None));
    }
}
