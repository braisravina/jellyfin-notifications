using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.EmailDigest.Delivery;
using Jellyfin.Plugin.EmailDigest.Email;
using Jellyfin.Plugin.EmailDigest.Services;
using Jellyfin.Plugin.EmailDigest.Subscription;
using Jellyfin.Plugin.EmailDigest.Tests.Delivery;
using Jellyfin.Plugin.EmailDigest.Verification;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.EmailDigest.Tests.Subscription;

/// <summary>
/// The whole Phase 7 journey for four synthetic users who stand in for the four accounts of the real
/// server: an administrator, a user with every library, a user with one library, and a user who never
/// confirms an address. Everything is in memory or in a temporary folder; no real account, preference or
/// address is read and no mail leaves the machine.
/// </summary>
public sealed class FourUsersEndToEndTests : IDisposable
{
    private readonly RunnerHarness _h = new();
    private readonly AddressVerificationService _verification;
    private readonly SubscriptionService _service;
    private readonly Guid _ana = Guid.NewGuid();
    private readonly Guid _bob = Guid.NewGuid();
    private readonly Guid _cai = Guid.NewGuid();
    private readonly Guid _dani = Guid.NewGuid();

    public FourUsersEndToEndTests()
    {
        _h.Config.SenderDisplayName = "Home Jellyfin";
        _verification = new AddressVerificationService(_h.Root, () => VerificationPolicy.ForLifetime(60), _h.Time, NullLogger.Instance);
        _service = new SubscriptionService(
            _h.Preferences,
            _verification,
            _h.Status,
            new NoSchedule(),
            new RunnerHarness.FixedConfig(_h.Config),
            _h.Passwords,
            _h.Transport,
            new VerificationMessageBuilder(new LocalizationService()),
            _h.Unsubscribe,
            _h.Users,
            _h.Time,
            NullLogger<SubscriptionService>.Instance);
        _h.Users.Add(_ana, new UserInfo("Ana (administrator)", false));
        _h.Users.Add(_bob, new UserInfo("Bob (all libraries)", false));
        _h.Users.Add(_cai, new UserInfo("Cai (one library)", false));
        _h.Users.Add(_dani, new UserInfo("Dani (never confirms)", false));
    }

    public void Dispose()
    {
        _verification.Dispose();
        _h.Dispose();
    }

    // Media added a few hours before the current run's upper bound, whatever day the test clock is at.
    private DateTimeOffset Ago(TimeSpan age) => _h.Time.GetUtcNow() - DigestRunner.SettleMargin - age;

    private static string TokenIn(OutgoingEmail email)
    {
        var start = email.TextBody.IndexOf("token=", StringComparison.Ordinal) + 6;
        return email.TextBody[start..email.TextBody.IndexOfAny(['\r', '\n'], start)];
    }

    private static string UnsubscribeTokenIn(OutgoingEmail email)
    {
        var url = email.UnsubscribeUrl!;
        return Uri.UnescapeDataString(url[(url.IndexOf("token=", StringComparison.Ordinal) + 6)..]);
    }

    private readonly System.Collections.Generic.List<OutgoingEmail> _earlier = new();

    private OutgoingEmail Verification(string address) => _earlier.Concat(_h.Transport.Sent.Select(s => s.Email)).Last(e => e.RecipientAddress == address);

    private void GiveMedia(string title, int minutesBeforeTheWindowEnds = 1)
    {
        var added = Ago(TimeSpan.FromMinutes(minutesBeforeTheWindowEnds));
        _h.Discovery.Give(_ana, RunnerHarness.Movie(title + " A", added), RunnerHarness.Movie(title + " B", added));
        _h.Discovery.Give(_bob, RunnerHarness.Movie(title + " A", added), RunnerHarness.Movie(title + " B", added));
        _h.Discovery.Give(_cai, RunnerHarness.Movie(title + " A", added));
        _h.Discovery.Give(_dani, RunnerHarness.Movie(title + " A", added));
    }

    private async Task SetUpAndConfirmAsync(Guid user, string address, string language)
    {
        await _service.SetLanguageAsync(user, language, CancellationToken.None);
        Assert.Equal(AddressRequestStatus.Sent, (await _service.RequestAddressAsync(user, address, CancellationToken.None)).Status);
        await _service.SetNotificationsAsync(user, true, CancellationToken.None);
        Assert.Equal(ConfirmOutcome.Confirmed, await _service.ConfirmAddressAsync(TokenIn(Verification(address)), CancellationToken.None));
    }

    [Fact]
    public async Task Journey_VerifiedUsersGetTheirOwnDigest_AnUnverifiedOneNever_AndUnsubscribeAndAddressChangeAreHonoured()
    {
        GiveMedia("First");

        // Everyone asks for notifications; only three prove their address.
        await SetUpAndConfirmAsync(_ana, "ana@example.test", "es");
        await SetUpAndConfirmAsync(_bob, "bob@example.test", "en");
        await SetUpAndConfirmAsync(_cai, "cai@example.test", "gl");
        await _service.SetNotificationsAsync(_dani, true, CancellationToken.None);
        await _service.RequestAddressAsync(_dani, "dani@example.test", CancellationToken.None);

        // Confirmation emails: one per request, to that address only, with no media or user details.
        var confirmations = _h.Transport.Sent.ToList();
        Assert.Equal(4, confirmations.Count);
        Assert.Equal(4, confirmations.Select(c => c.Email.RecipientAddress).Distinct().Count());
        Assert.All(confirmations, c =>
        {
            var all = c.Email.Subject + c.Email.TextBody + c.Email.HtmlBody;
            Assert.DoesNotContain("First", all, StringComparison.Ordinal);
            Assert.DoesNotContain("(administrator)", all, StringComparison.Ordinal);
            Assert.DoesNotContain("example.test", all.Replace(c.Email.RecipientAddress, string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        });
        _earlier.AddRange(confirmations.Select(c => c.Email));
        _h.Transport.Sent.Clear();

        // First run: three digests, each to its own verified address, in its own language and library view.
        var first = await _h.RunAsync();

        Assert.Equal(3, first.MessagesAccepted);
        Assert.Equal(new[] { "ana@example.test", "bob@example.test", "cai@example.test" }, _h.Transport.Sent.Select(s => s.Email.RecipientAddress).OrderBy(a => a, StringComparer.Ordinal).ToArray());
        string Sent(string to) => _h.Transport.Sent.Single(s => s.Email.RecipientAddress == to).Email.TextBody;
        Assert.Contains("First B", Sent("bob@example.test"), StringComparison.Ordinal);
        Assert.DoesNotContain("First B", Sent("cai@example.test"), StringComparison.Ordinal);
        Assert.StartsWith("Resumen de Jellyfin", _h.Transport.Sent.Single(s => s.Email.RecipientAddress == "ana@example.test").Email.Subject, StringComparison.Ordinal);
        Assert.Null(await _h.State.GetAsync(_dani, CancellationToken.None));

        // Every digest's unsubscribe link belongs to exactly its own recipient.
        foreach (var (user, address) in new[] { (_ana, "ana@example.test"), (_bob, "bob@example.test"), (_cai, "cai@example.test") })
        {
            Assert.Equal(user, _h.Unsubscribe.TryRead(UnsubscribeTokenIn(_h.Transport.Sent.Single(s => s.Email.RecipientAddress == address).Email)));
        }

        // Personal status: accepted for the three, nothing for the one who never received.
        Assert.NotNull((await _service.GetOverviewAsync(_bob, CancellationToken.None))!.LastAcceptedUtc);
        Assert.Null((await _service.GetOverviewAsync(_dani, CancellationToken.None))!.LastAcceptedUtc);

        // Bob unsubscribes from the link in his digest; the others are untouched.
        var caiBefore = (await _h.State.GetAsync(_cai, CancellationToken.None))!.CheckpointUtc;
        Assert.True(await _service.UnsubscribeAsync(UnsubscribeTokenIn(_h.Transport.Sent.Single(s => s.Email.RecipientAddress == "bob@example.test").Email), CancellationToken.None));

        // Cai asks to move to a new address; until confirmed, digests still go to the old one.
        _h.Time.Advance(TimeSpan.FromMinutes(2)); // past the one-request-a-minute limit
        Assert.Equal(AddressRequestStatus.Sent, (await _service.RequestAddressAsync(_cai, "cai.new@example.test", CancellationToken.None)).Status);
        _h.Time.Advance(TimeSpan.FromMinutes(10)); // well inside the confirmation link's hour
        GiveMedia("Second");
        var beforeSecond = _h.Transport.Sent.Count;

        var second = await _h.RunAsync();

        var sentSecond = _h.Transport.Sent.Skip(beforeSecond).Select(s => s.Email.RecipientAddress).OrderBy(a => a, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "ana@example.test", "cai@example.test" }, sentSecond);
        Assert.Equal(2, second.MessagesAccepted);
        Assert.True((await _h.State.GetAsync(_cai, CancellationToken.None))!.CheckpointUtc > caiBefore);

        // Cai confirms the new address; the next digest goes there and nowhere else.
        await _service.ConfirmAddressAsync(TokenIn(Verification("cai.new@example.test")), CancellationToken.None);
        _h.Time.Advance(TimeSpan.FromDays(1));
        GiveMedia("Third");
        var beforeThird = _h.Transport.Sent.Count;

        await _h.RunAsync();

        var third = _h.Transport.Sent.Skip(beforeThird).Select(s => s.Email.RecipientAddress).OrderBy(a => a, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "ana@example.test", "cai.new@example.test" }, third);

        // Dani's first link expired long ago; it is refused, so Dani asks again and confirms the new one.
        Assert.Equal(ConfirmOutcome.Invalid, await _service.ConfirmAddressAsync(TokenIn(Verification("dani@example.test")), CancellationToken.None));
        Assert.Equal(AddressRequestStatus.Sent, (await _service.RequestAddressAsync(_dani, "dani@example.test", CancellationToken.None)).Status);
        Assert.Equal(ConfirmOutcome.Confirmed, await _service.ConfirmAddressAsync(TokenIn(Verification("dani@example.test")), CancellationToken.None));
        _h.Time.Advance(TimeSpan.FromDays(1));
        GiveMedia("Fourth");
        var beforeFourth = _h.Transport.Sent.Count;

        await _h.RunAsync();

        Assert.Contains("dani@example.test", _h.Transport.Sent.Skip(beforeFourth).Select(s => s.Email.RecipientAddress));
        Assert.DoesNotContain("bob@example.test", _h.Transport.Sent.Skip(beforeSecond).Select(s => s.Email.RecipientAddress));
    }

    [Fact]
    public async Task EachUser_CanChangeOnlyTheirOwnPreferences()
    {
        await SetUpAndConfirmAsync(_ana, "ana@example.test", "es");
        await SetUpAndConfirmAsync(_bob, "bob@example.test", "en");

        await _service.SetNotificationsAsync(_ana, false, CancellationToken.None);
        await _service.SetLanguageAsync(_ana, "gl", CancellationToken.None);

        var bob = (await _h.Preferences.GetAsync(_bob, CancellationToken.None))!;
        Assert.True(bob.NotificationsEnabled);
        Assert.Equal("en", bob.Language);
        Assert.Equal("bob@example.test", bob.Email);
    }

    [Fact]
    public async Task ADisabledAccount_CannotChangeAnything_AndIsNotSentAnything()
    {
        await SetUpAndConfirmAsync(_dani, "dani@example.test", "en");
        _h.Users.Add(_dani, new UserInfo("Dani (never confirms)", true));
        _h.Discovery.Give(_dani, RunnerHarness.Movie("Film", Ago(TimeSpan.FromHours(2))));
        _h.Transport.Sent.Clear();

        Assert.Equal(SubscriptionChange.UserUnavailable, await _service.SetNotificationsAsync(_dani, false, CancellationToken.None));
        await _h.RunAsync();

        Assert.Empty(_h.Transport.Sent);
    }

    private sealed class NoSchedule : IScheduleInfo
    {
        public DateTimeOffset? GetNextRunUtc() => null;
    }
}
