using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.EmailDigest.Delivery;
using Jellyfin.Plugin.EmailDigest.Email;
using Jellyfin.Plugin.EmailDigest.Models;
using Xunit;

namespace Jellyfin.Plugin.EmailDigest.Tests.Delivery;

/// <summary>
/// Phase 7: the runner honours verified addresses, preference changes made while it runs, the
/// unsubscribe link, and records each user's own delivery status.
/// </summary>
public sealed class DigestRunnerPreferenceTests : IDisposable
{
    private static readonly DateTimeOffset Upper = RunnerHarness.Start - DigestRunner.SettleMargin;

    private readonly RunnerHarness _h = new();

    public void Dispose()
    {
        _h.Dispose();
    }

    private static DateTimeOffset Ago(int hours) => Upper.AddHours(-hours);

    private static EmailTransportResult Result(EmailTransportOutcome outcome) => new(outcome);

    private async Task SetAsync(Guid user, Action<UserNotificationPreferences> change)
    {
        await _h.Preferences.UpdateAsync(user, p => { change(p!); return p; }, CancellationToken.None);
    }

    // Unsubscribe link and footer

    [Fact]
    public async Task EveryDigest_CarriesTheRecipientsOwnUnsubscribeLink_InTheHeaderField_AndTheFooter()
    {
        var ana = await _h.AddUserAsync("Ana", "ana@example.com");
        var bob = await _h.AddUserAsync("Bob", "bob@example.com");
        _h.Discovery.Give(ana, RunnerHarness.Movie("Heat", Ago(2)));
        _h.Discovery.Give(bob, RunnerHarness.Movie("Heat", Ago(2)));

        await _h.RunAsync();

        foreach (var (id, address) in new[] { (ana, "ana@example.com"), (bob, "bob@example.com") })
        {
            var email = _h.Transport.Sent.Single(s => s.Email.RecipientAddress == address).Email;
            var url = email.UnsubscribeUrl!;
            Assert.StartsWith("https://jellyfin.example.org/EmailDigest/Unsubscribe?token=", url, StringComparison.Ordinal);
            var token = Uri.UnescapeDataString(url[(url.IndexOf("token=", StringComparison.Ordinal) + 6)..]);
            Assert.Equal(id, _h.Unsubscribe.TryRead(token));
            Assert.Contains(url, email.TextBody, StringComparison.Ordinal);
            Assert.Contains("href=\"" + url + "\"", email.HtmlBody, StringComparison.Ordinal);
            Assert.Contains("https://jellyfin.example.org/EmailDigest/Me/Page", email.TextBody, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task DryRun_BuildsNoLinks_AndRecordsNoStatus()
    {
        _h.Config.DigestDryRun = true;
        var user = await _h.AddUserAsync("Ana", "ana@example.com");
        _h.Discovery.Give(user, RunnerHarness.Movie("Heat", Ago(2)));

        var summary = await _h.RunAsync();

        Assert.Equal(1, summary.DigestsRenderedDryRun);
        Assert.Null(await _h.Status.GetAsync(user, CancellationToken.None));
        Assert.False(File.Exists(Path.Combine(_h.Root, "unsubscribe.key")));
    }

    [Fact]
    public async Task LiveRun_WithoutAPublicAddress_SendsNothing()
    {
        _h.Config.PublicBaseUrl = string.Empty;
        var user = await _h.AddUserAsync("Ana", "ana@example.com");
        _h.Discovery.Give(user, RunnerHarness.Movie("Heat", Ago(2)));

        var summary = await _h.RunAsync();

        Assert.Equal(DigestRunOutcome.Aborted, summary.Outcome);
        Assert.Equal("public-url-missing", summary.Reason);
        Assert.Empty(_h.Transport.Sent);
        Assert.Null(await _h.State.GetAsync(user, CancellationToken.None));
    }

    [Fact]
    public async Task DryRun_WithoutAPublicAddress_StillWorks()
    {
        _h.Config.PublicBaseUrl = string.Empty;
        _h.Config.DigestDryRun = true;
        var user = await _h.AddUserAsync("Ana", "ana@example.com");
        _h.Discovery.Give(user, RunnerHarness.Movie("Heat", Ago(2)));

        Assert.Equal(DigestRunOutcome.Completed, (await _h.RunAsync()).Outcome);
    }

    [Fact]
    public async Task LiveRun_WithAnUnusableUnsubscribeKey_SendsNothing_AndLeavesTheKeyAlone()
    {
        await File.WriteAllBytesAsync(Path.Combine(_h.Root, "unsubscribe.key"), new byte[3]);
        var user = await _h.AddUserAsync("Ana", "ana@example.com");
        _h.Discovery.Give(user, RunnerHarness.Movie("Heat", Ago(2)));

        var summary = await _h.RunAsync();

        Assert.Equal("unsubscribe-unavailable", summary.Reason);
        Assert.Empty(_h.Transport.Sent);
        Assert.Equal(3, new FileInfo(Path.Combine(_h.Root, "unsubscribe.key")).Length);
    }

    // Preferences changed while the run is in progress

    [Fact]
    public async Task UserUnsubscribedDuringTheRun_BeforeTheirTurn_IsNotSent_AndKeepsTheirCheckpoint()
    {
        var a = await _h.AddUserAsync("Ana", "ana@example.com");
        var b = await _h.AddUserAsync("Bob", "bob@example.com");
        var (first, second) = a.CompareTo(b) < 0 ? (a, b) : (b, a);
        var firstAddress = first == a ? "ana@example.com" : "bob@example.com";
        _h.Discovery.Give(first, RunnerHarness.Movie("Heat", Ago(2)));
        _h.Discovery.Give(second, RunnerHarness.Movie("Heat", Ago(2)));
        var before = Ago(48);
        await _h.State.SaveAsync(new UserDeliveryState { UserId = second, CheckpointUtc = before }, CancellationToken.None);
        _h.Transport.BeforeAnswer = async (email, _) =>
        {
            if (email.RecipientAddress == firstAddress)
            {
                await SetAsync(second, p => p.NotificationsEnabled = false);
            }
        };

        var summary = await _h.RunAsync();

        Assert.Single(_h.Transport.Sent);
        Assert.Equal(firstAddress, _h.Transport.Sent[0].Email.RecipientAddress);
        Assert.Equal(before, (await _h.State.GetAsync(second, CancellationToken.None))!.CheckpointUtc);
        Assert.Equal(1, summary.UsersSkipped);
        Assert.Equal(1, summary.MessagesAccepted);
        Assert.Equal(DigestRunOutcome.Completed, summary.Outcome);
    }

    [Fact]
    public async Task UserUnsubscribedBetweenRetries_IsNotSentAgain()
    {
        var user = await _h.AddUserAsync("Ana", "ana@example.com");
        _h.Discovery.Give(user, RunnerHarness.Movie("Heat", Ago(2)));
        _h.Transport.Behavior = (_, attempt) => attempt == 1 ? Result(EmailTransportOutcome.ConnectionFailed) : Result(EmailTransportOutcome.Accepted);
        _h.Transport.BeforeAnswer = async (_, _) => await SetAsync(user, p => p.NotificationsEnabled = false);

        var summary = await _h.RunAsync();

        Assert.Single(_h.Transport.Sent);
        Assert.Equal(0, summary.MessagesAccepted);
        Assert.Null(await _h.State.GetAsync(user, CancellationToken.None));
        Assert.Equal(DigestRunOutcome.Completed, summary.Outcome);
    }

    [Fact]
    public async Task AddressChangedToAnotherVerifiedOne_BetweenRetries_GoesToTheNewAddressOnly()
    {
        var user = await _h.AddUserAsync("Ana", "old@example.com");
        _h.Discovery.Give(user, RunnerHarness.Movie("Heat", Ago(2)));
        _h.Transport.Behavior = (email, _) => email.RecipientAddress == "old@example.com" ? Result(EmailTransportOutcome.ConnectionFailed) : Result(EmailTransportOutcome.Accepted);
        _h.Transport.BeforeAnswer = async (email, _) =>
        {
            if (email.RecipientAddress == "old@example.com")
            {
                await SetAsync(user, p => p.Email = "new@example.com");
            }
        };

        await _h.RunAsync();

        Assert.Equal(new[] { "old@example.com", "new@example.com" }, _h.Transport.Sent.Select(s => s.Email.RecipientAddress).ToArray());
        Assert.NotNull(await _h.State.GetAsync(user, CancellationToken.None));
    }

    [Fact]
    public async Task AddressNoLongerVerified_BetweenRetries_IsNotSent()
    {
        var user = await _h.AddUserAsync("Ana", "ana@example.com");
        _h.Discovery.Give(user, RunnerHarness.Movie("Heat", Ago(2)));
        _h.Transport.Behavior = (_, attempt) => attempt == 1 ? Result(EmailTransportOutcome.ConnectionFailed) : Result(EmailTransportOutcome.Accepted);
        _h.Transport.BeforeAnswer = async (_, _) => await SetAsync(user, p => p.EmailVerifiedUtc = null);

        await _h.RunAsync();

        Assert.Single(_h.Transport.Sent);
        Assert.Null(await _h.State.GetAsync(user, CancellationToken.None));
    }

    [Fact]
    public async Task LanguageChangedBetweenRetries_IsUsedForTheNextAttempt()
    {
        var user = await _h.AddUserAsync("Ana", "ana@example.com", "en");
        _h.Discovery.Give(user, RunnerHarness.Movie("Heat", Ago(2)));
        _h.Transport.Behavior = (_, attempt) => attempt == 1 ? Result(EmailTransportOutcome.ConnectionFailed) : Result(EmailTransportOutcome.Accepted);
        _h.Transport.BeforeAnswer = async (_, _) => await SetAsync(user, p => p.Language = "gl");

        await _h.RunAsync();

        Assert.StartsWith("Jellyfin digest", _h.Transport.Sent[0].Email.Subject, StringComparison.Ordinal);
        Assert.False(_h.Transport.Sent[1].Email.Subject.StartsWith("Jellyfin digest", StringComparison.Ordinal));
        Assert.Equal(_h.Transport.Sent[0].Email.MessageIdLocalPart, _h.Transport.Sent[1].Email.MessageIdLocalPart);
    }

    [Fact]
    public async Task PreferencesFileBecomingUnreadableMidRun_SendsNothingFurther()
    {
        var user = await _h.AddUserAsync("Ana", "ana@example.com");
        _h.Discovery.Give(user, RunnerHarness.Movie("Heat", Ago(2)));
        _h.Transport.Behavior = (_, _) => Result(EmailTransportOutcome.ConnectionFailed);
        _h.Transport.BeforeAnswer = async (_, token) => await File.WriteAllTextAsync(Path.Combine(_h.Root, "user-preferences.json"), "{ broken", token);

        await _h.RunAsync();

        Assert.Single(_h.Transport.Sent);
        Assert.Null(await _h.State.GetAsync(user, CancellationToken.None));
    }

    [Fact]
    public async Task UserDisabledInJellyfinMidRun_IsNotSent()
    {
        var user = await _h.AddUserAsync("Ana", "ana@example.com");
        _h.Discovery.Give(user, RunnerHarness.Movie("Heat", Ago(2)));
        _h.Transport.Behavior = (_, attempt) => attempt == 1 ? Result(EmailTransportOutcome.ConnectionFailed) : Result(EmailTransportOutcome.Accepted);
        _h.Transport.BeforeAnswer = (_, _) =>
        {
            _h.Users.Add(user, new UserInfo("Ana", true));
            return Task.CompletedTask;
        };

        await _h.RunAsync();

        Assert.Single(_h.Transport.Sent);
    }

    [Fact]
    public async Task SkippedUser_KeepsTheirCheckpoint_AndIsCaughtUpWhenTheyReturn()
    {
        var user = await _h.AddUserAsync("Ana", "ana@example.com");
        _h.Discovery.Give(user, RunnerHarness.Movie("First", Ago(2)));
        await _h.RunAsync();
        var checkpoint = (await _h.State.GetAsync(user, CancellationToken.None))!.CheckpointUtc;

        await SetAsync(user, p => p.NotificationsEnabled = false);
        _h.Time.Advance(TimeSpan.FromDays(2));
        _h.Discovery.Give(user, RunnerHarness.Movie("While off", RunnerHarness.Start.AddHours(1)));
        await _h.RunAsync();
        Assert.Equal(checkpoint, (await _h.State.GetAsync(user, CancellationToken.None))!.CheckpointUtc);

        await SetAsync(user, p => p.NotificationsEnabled = true);
        _h.Time.Advance(TimeSpan.FromDays(1));
        await _h.RunAsync();

        Assert.Contains("While off", _h.Transport.Sent.Last().Email.TextBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OneUsersUnsubscribe_NeverChangesAnotherUsersState()
    {
        var a = await _h.AddUserAsync("Ana", "ana@example.com");
        var b = await _h.AddUserAsync("Bob", "bob@example.com");
        _h.Discovery.Give(a, RunnerHarness.Movie("Heat", Ago(2)));
        _h.Discovery.Give(b, RunnerHarness.Movie("Heat", Ago(2)));
        await _h.RunAsync();
        var bobBefore = (await _h.State.GetAsync(b, CancellationToken.None))!.CheckpointUtc;

        await SetAsync(a, p => p.NotificationsEnabled = false);
        _h.Time.Advance(TimeSpan.FromDays(1));
        _h.Discovery.Give(a, RunnerHarness.Movie("New", RunnerHarness.Start.AddHours(1)));
        _h.Discovery.Give(b, RunnerHarness.Movie("New", RunnerHarness.Start.AddHours(1)));
        await _h.RunAsync();

        Assert.True((await _h.State.GetAsync(b, CancellationToken.None))!.CheckpointUtc > bobBefore);
        Assert.Equal(1, _h.Transport.Sent.Count(s => s.Email.RecipientAddress == "ana@example.com"));
    }

    // Personal status

    [Fact]
    public async Task AcceptedDigest_IsRecordedForThatUserOnly()
    {
        var a = await _h.AddUserAsync("Ana", "ana@example.com");
        var b = await _h.AddUserAsync("Bob", "bob@example.com");
        _h.Discovery.Give(a, RunnerHarness.Movie("Heat", Ago(2)));

        await _h.RunAsync();

        var status = await _h.Status.GetAsync(a, CancellationToken.None);
        Assert.Equal(DeliveryAttemptStatus.Accepted, status!.LastAttemptStatus);
        Assert.Equal(RunnerHarness.Start, status.LastAcceptedUtc);
        Assert.Null(await _h.Status.GetAsync(b, CancellationToken.None));
    }

    [Fact]
    public async Task FailedDigest_IsRecordedAsFailed_KeepingTheEarlierAcceptedTime()
    {
        var user = await _h.AddUserAsync("Ana", "ana@example.com");
        _h.Discovery.Give(user, RunnerHarness.Movie("First", Ago(2)));
        await _h.RunAsync();
        _h.Time.Advance(TimeSpan.FromDays(1));
        _h.Discovery.Give(user, RunnerHarness.Movie("Second", RunnerHarness.Start.AddHours(1)));
        _h.Transport.Behavior = (_, _) => Result(EmailTransportOutcome.ConnectionFailed);

        await _h.RunAsync();

        var status = await _h.Status.GetAsync(user, CancellationToken.None);
        Assert.Equal(DeliveryAttemptStatus.Failed, status!.LastAttemptStatus);
        Assert.Equal(RunnerHarness.Start, status.LastAcceptedUtc);
    }

    [Fact]
    public async Task UncertainDigest_IsRecordedAsUncertain_NotAsAccepted()
    {
        var user = await _h.AddUserAsync("Ana", "ana@example.com");
        _h.Discovery.Give(user, RunnerHarness.Movie("Heat", Ago(2)));
        _h.Transport.Behavior = (_, _) => Result(EmailTransportOutcome.Uncertain);

        await _h.RunAsync();

        var status = await _h.Status.GetAsync(user, CancellationToken.None);
        Assert.Equal(DeliveryAttemptStatus.Uncertain, status!.LastAttemptStatus);
        Assert.Null(status.LastAcceptedUtc);
    }

    [Fact]
    public async Task DiscoveryFailure_IsRecordedAsFailed()
    {
        var user = await _h.AddUserAsync("Ana", "ana@example.com");
        _h.Discovery.FailFor(user);

        await _h.RunAsync();

        Assert.Equal(DeliveryAttemptStatus.Failed, (await _h.Status.GetAsync(user, CancellationToken.None))!.LastAttemptStatus);
    }

    [Fact]
    public async Task SkippedAndEmptyUsers_RecordNoAttempt()
    {
        var off = await _h.AddUserAsync("Ana", "ana@example.com", notifications: false);
        var none = await _h.AddUserAsync("Bob", "bob@example.com");

        await _h.RunAsync();

        Assert.Null(await _h.Status.GetAsync(off, CancellationToken.None));
        Assert.Null(await _h.Status.GetAsync(none, CancellationToken.None));
    }

    [Fact]
    public async Task StatusStoreFailing_NeverChangesWhatIsSentOrRecorded()
    {
        File.Delete(Path.Combine(_h.Root, "delivery-status.json"));
        Directory.CreateDirectory(Path.Combine(_h.Root, "delivery-status.json"));
        var user = await _h.AddUserAsync("Ana", "ana@example.com");
        _h.Discovery.Give(user, RunnerHarness.Movie("Heat", Ago(2)));

        var summary = await _h.RunAsync();

        Assert.Equal(1, summary.MessagesAccepted);
        Assert.NotNull(await _h.State.GetAsync(user, CancellationToken.None));
        Assert.Equal(DigestRunOutcome.Completed, summary.Outcome);
    }
}
