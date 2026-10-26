using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.EmailDigest.Configuration;
using Jellyfin.Plugin.EmailDigest.Delivery;
using Jellyfin.Plugin.EmailDigest.Digest;
using Jellyfin.Plugin.EmailDigest.Email;
using Jellyfin.Plugin.EmailDigest.Models;
using Jellyfin.Plugin.EmailDigest.Services;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.EmailDigest.Tests.Delivery;

public sealed class DigestRunnerTests : IDisposable
{
    private static readonly DateTimeOffset Upper = RunnerHarness.Start - DigestRunner.SettleMargin;

    private readonly RunnerHarness _h = new();

    public void Dispose()
    {
        _h.Dispose();
    }

    private static DateTimeOffset Ago(int hours)
    {
        return Upper.AddHours(-hours);
    }

    private static EmailTransportResult Result(EmailTransportOutcome outcome, int? code = null)
    {
        return new EmailTransportResult(outcome, code);
    }

    // Success

    [Fact]
    public async Task SingleUser_NewMedia_OneMessageSentToThatUser_AndOnlyThenTheirCheckpointAdvances()
    {
        var user = await _h.AddUserAsync("Ana", "ana@example.com");
        var movie = RunnerHarness.Movie("Heat", Ago(2));
        _h.Discovery.Give(user, movie);

        var summary = await _h.RunAsync();

        var (settings, email) = Assert.Single(_h.Transport.Sent);
        Assert.Equal("ana@example.com", email.RecipientAddress);
        Assert.Equal("Ana", email.RecipientName);
        Assert.Contains("Heat", email.TextBody, StringComparison.Ordinal);
        Assert.Equal("smtp.example.com", settings.Host);
        Assert.Equal(DigestRunOutcome.Completed, summary.Outcome);
        Assert.Equal(1, summary.MessagesAccepted);
        var state = await _h.State.GetAsync(user, CancellationToken.None);
        Assert.Equal(Upper, state!.CheckpointUtc);
        Assert.Equal(movie.ItemId, Assert.Single(state.Delivered).ItemId);
    }

    [Fact]
    public async Task Message_CarriesADeterministicIdThatIsTheSameForTheSameDigest()
    {
        var user = await _h.AddUserAsync("Ana", "ana@example.com");
        _h.Discovery.Give(user, RunnerHarness.Movie("Heat", Ago(2)));
        _h.Transport.Behavior = (_, attempt) => attempt < 2 ? Result(EmailTransportOutcome.ConnectionFailed) : Result(EmailTransportOutcome.Accepted);

        await _h.RunAsync();

        var ids = _h.Transport.Sent.Select(s => s.Email.MessageIdLocalPart).ToList();
        Assert.Equal(2, ids.Count);
        Assert.NotNull(ids[0]);
        Assert.Equal(ids[0], ids[1]);
    }

    [Fact]
    public async Task EachUserGetsTheirOwnLanguage_AndAnUnsupportedOneFallsBackToTheDefault()
    {
        _h.Config.DefaultLanguage = "gl";
        var es = await _h.AddUserAsync("Ana", "ana@example.com", "es");
        var en = await _h.AddUserAsync("Bob", "bob@example.com", "en");
        var pref = await _h.AddUserAsync("Cai", "cai@example.com", "en");
        await _h.Preferences.SaveAsync(new UserNotificationPreferences { EmailVerifiedUtc = RunnerHarness.Verified, UserId = pref, Email = "cai@example.com", Language = "en", NotificationsEnabled = true }, CancellationToken.None);
        foreach (var u in new[] { es, en, pref })
        {
            _h.Discovery.Give(u, RunnerHarness.Movie("Heat", Ago(2)));
        }

        await _h.RunAsync();

        string Subject(string to) => _h.Transport.Sent.Single(s => s.Email.RecipientAddress == to).Email.Subject;
        Assert.StartsWith("Resumen de Jellyfin", Subject("ana@example.com"), StringComparison.Ordinal);
        Assert.StartsWith("Jellyfin digest", Subject("bob@example.com"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task UsersWithDifferentLibraryPermissions_NeverSeeEachOthersMedia()
    {
        var all = await _h.AddUserAsync("Ana", "ana@example.com");
        var limited = await _h.AddUserAsync("Bob", "bob@example.com");
        _h.Discovery.Give(all, RunnerHarness.Movie("Public Film", Ago(2)), RunnerHarness.Movie("Secret Library Film", Ago(2)));
        _h.Discovery.Give(limited, RunnerHarness.Movie("Public Film", Ago(2)));

        await _h.RunAsync();

        var bobMail = _h.Transport.Sent.Single(s => s.Email.RecipientAddress == "bob@example.com").Email;
        var anaMail = _h.Transport.Sent.Single(s => s.Email.RecipientAddress == "ana@example.com").Email;
        Assert.DoesNotContain("Secret Library Film", bobMail.TextBody + bobMail.HtmlBody, StringComparison.Ordinal);
        Assert.DoesNotContain("bob@example.com", anaMail.TextBody + anaMail.HtmlBody, StringComparison.Ordinal);
        Assert.Contains("Secret Library Film", anaMail.TextBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EveryMessageIsSeparate_OneRecipientEach()
    {
        var a = await _h.AddUserAsync("Ana", "ana@example.com");
        var b = await _h.AddUserAsync("Bob", "bob@example.com");
        _h.Discovery.Give(a, RunnerHarness.Movie("One", Ago(2)));
        _h.Discovery.Give(b, RunnerHarness.Movie("One", Ago(2)));

        await _h.RunAsync();

        Assert.Equal(2, _h.Transport.Sent.Count);
        Assert.Equal(2, _h.Transport.Sent.Select(s => s.Email.RecipientAddress).Distinct().Count());
        Assert.NotEqual(_h.Transport.Sent[0].Email.MessageIdLocalPart, _h.Transport.Sent[1].Email.MessageIdLocalPart);
    }

    // Who is eligible

    [Fact]
    public async Task NotificationsOff_UserIsSkipped_NothingIsDiscoveredAndNothingIsChanged()
    {
        var user = await _h.AddUserAsync("Ana", "ana@example.com", notifications: false);
        _h.Discovery.Give(user, RunnerHarness.Movie("Heat", Ago(2)));

        var summary = await _h.RunAsync();

        Assert.Empty(_h.Transport.Sent);
        Assert.Empty(_h.Discovery.Calls);
        Assert.Null(await _h.State.GetAsync(user, CancellationToken.None));
        Assert.Equal(1, summary.UsersSkipped);
        Assert.Equal(0, summary.UsersEvaluated);
    }

    [Fact]
    public async Task UnknownAndDisabledJellyfinAccounts_AreSkippedWithTheirStateUntouched()
    {
        var disabled = await _h.AddUserAsync("Ana", "ana@example.com", disabledAccount: true);
        var gone = Guid.NewGuid();
        await _h.Preferences.SaveAsync(new UserNotificationPreferences { EmailVerifiedUtc = RunnerHarness.Verified, UserId = gone, Email = "gone@example.com", NotificationsEnabled = true }, CancellationToken.None);
        _h.Discovery.Give(disabled, RunnerHarness.Movie("Heat", Ago(2)));

        var summary = await _h.RunAsync();

        Assert.Empty(_h.Transport.Sent);
        Assert.Equal(2, summary.UsersSkipped);
        Assert.Equal(DigestRunOutcome.Completed, summary.Outcome);
        Assert.Empty(await _h.State.GetAllAsync(CancellationToken.None));
    }

    [Fact]
    public async Task InvalidStoredAddress_IsSkippedAndNothingIsSent()
    {
        var user = Guid.NewGuid();
        // Written straight to the file, as a hand-edited preferences file could contain.
        await File.WriteAllTextAsync(
            Path.Combine(_h.Root, "user-preferences.json"),
            JsonSerializer.Serialize(new[] { new UserNotificationPreferences { UserId = user, Email = "not-an-address", Language = "en", NotificationsEnabled = true } }));
        _h.Users.Add(user, new UserInfo("Ana", false));
        _h.Discovery.Give(user, RunnerHarness.Movie("Heat", Ago(2)));

        var summary = await _h.RunAsync();

        Assert.Empty(_h.Transport.Sent);
        Assert.Equal(1, summary.UsersSkipped);
    }

    [Fact]
    public async Task UnverifiedAddress_NeverReceivesADigest_AndKeepsItsState()
    {
        var user = Guid.NewGuid();
        await _h.Preferences.SaveAsync(new UserNotificationPreferences { UserId = user, Email = "ana@example.com", Language = "en", NotificationsEnabled = true }, CancellationToken.None);
        _h.Users.Add(user, new UserInfo("Ana", false));
        _h.Discovery.Give(user, RunnerHarness.Movie("Heat", Ago(2)));

        var summary = await _h.RunAsync();

        Assert.Empty(_h.Transport.Sent);
        Assert.Empty(_h.Discovery.Calls);
        Assert.Empty(await _h.State.GetAllAsync(CancellationToken.None));
        Assert.Equal(1, summary.UsersSkipped);
        Assert.Equal(DigestRunOutcome.Completed, summary.Outcome);
    }

    [Fact]
    public async Task DamagedPreferencesFile_AbortsTheRunAndIsLeftUntouched()
    {
        var path = Path.Combine(_h.Root, "user-preferences.json");
        await File.WriteAllTextAsync(path, "{ this is not json");

        var summary = await _h.RunAsync();

        Assert.Equal(DigestRunOutcome.Aborted, summary.Outcome);
        Assert.Equal("preferences-unreadable", summary.Reason);
        Assert.Empty(_h.Transport.Sent);
        Assert.Equal("{ this is not json", await File.ReadAllTextAsync(path));
    }

    // Nothing new

    [Fact]
    public async Task NothingNew_SendsNothing_AndAdvancesTheCheckpointBecauseTheWindowWasExamined()
    {
        var user = await _h.AddUserAsync("Ana", "ana@example.com");

        var summary = await _h.RunAsync();

        Assert.Empty(_h.Transport.Sent);
        Assert.Equal(1, summary.UsersWithNoNewMedia);
        Assert.Equal(Upper, (await _h.State.GetAsync(user, CancellationToken.None))!.CheckpointUtc);
    }

    [Fact]
    public async Task NormalSuccessiveRuns_NeverRepeatAnItem()
    {
        var user = await _h.AddUserAsync("Ana", "ana@example.com");
        _h.Discovery.Give(user, RunnerHarness.Movie("Heat", Ago(2)));

        await _h.RunAsync();
        _h.Time.Advance(TimeSpan.FromHours(24));
        await _h.RunAsync();
        _h.Time.Advance(TimeSpan.FromHours(24));
        await _h.RunAsync();

        Assert.Single(_h.Transport.Sent);
    }

    [Fact]
    public async Task MediaAddedDuringTheSettleMargin_IsNotSkipped_ItArrivesInTheNextRun()
    {
        var user = await _h.AddUserAsync("Ana", "ana@example.com");
        var early = RunnerHarness.Movie("Early", Ago(2));
        var late = RunnerHarness.Movie("Late", Upper.AddMinutes(2));
        _h.Discovery.Give(user, early, late);

        await _h.RunAsync();
        Assert.DoesNotContain("Late", _h.Transport.Sent.Single().Email.TextBody, StringComparison.Ordinal);
        Assert.Equal(Upper, (await _h.State.GetAsync(user, CancellationToken.None))!.CheckpointUtc);

        _h.Time.Advance(TimeSpan.FromHours(1));
        await _h.RunAsync();

        Assert.Equal(2, _h.Transport.Sent.Count);
        Assert.Contains("Late", _h.Transport.Sent[1].Email.TextBody, StringComparison.Ordinal);
        Assert.DoesNotContain("Early", _h.Transport.Sent[1].Email.TextBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReImportedMedia_SameIdNewDate_IsNotAnnouncedAgain()
    {
        var user = await _h.AddUserAsync("Ana", "ana@example.com");
        var id = Guid.NewGuid();
        _h.Discovery.Give(user, RunnerHarness.Movie("Heat", Ago(2), id: id));
        await _h.RunAsync();

        _h.Time.Advance(TimeSpan.FromHours(24));
        _h.Discovery.Give(user, RunnerHarness.Movie("Heat", Upper.AddHours(3), id: id));
        var summary = await _h.RunAsync();

        Assert.Single(_h.Transport.Sent);
        Assert.Equal(1, summary.UsersWithNoNewMedia);
    }

    // Display limits never lose notifications

    [Fact]
    public async Task HiddenEntries_StayPending_AndArriveInTheNextDigestWithoutDuplicates()
    {
        _h.Config.MaxEntriesPerSection = 2;
        _h.Config.MaxEntriesPerDigest = 2;
        var user = await _h.AddUserAsync("Ana", "ana@example.com");
        var movies = Enumerable.Range(1, 5).Select(i => RunnerHarness.Movie("Movie " + i, Ago(10 - i))).ToArray();
        _h.Discovery.Give(user, movies);

        await _h.RunAsync();
        _h.Time.Advance(TimeSpan.FromHours(24));
        await _h.RunAsync();
        _h.Time.Advance(TimeSpan.FromHours(24));
        await _h.RunAsync();
        _h.Time.Advance(TimeSpan.FromHours(24));
        var last = await _h.RunAsync();

        Assert.Equal(3, _h.Transport.Sent.Count);
        var titles = _h.Transport.Sent.SelectMany(s => Enumerable.Range(1, 5).Select(i => "Movie " + i).Where(t => s.Email.TextBody.Contains(t, StringComparison.Ordinal))).ToList();
        Assert.Equal(5, titles.Count);
        Assert.Equal(5, titles.Distinct().Count());
        Assert.Contains("next digest", _h.Transport.Sent[0].Email.TextBody, StringComparison.Ordinal);
        Assert.DoesNotContain("next digest", _h.Transport.Sent[2].Email.TextBody, StringComparison.Ordinal);
        Assert.Equal(1, last.UsersWithNoNewMedia);
    }

    [Fact]
    public async Task PartialAdvance_CheckpointStopsJustBeforeTheOldestHiddenItem()
    {
        _h.Config.MaxEntriesPerSection = 1;
        _h.Config.MaxEntriesPerDigest = 1;
        var user = await _h.AddUserAsync("Ana", "ana@example.com");
        var a = RunnerHarness.Movie("A first alphabetically", Ago(1));
        var z = RunnerHarness.Movie("Z hidden", Ago(5));
        _h.Discovery.Give(user, a, z);

        await _h.RunAsync();

        var state = await _h.State.GetAsync(user, CancellationToken.None);
        Assert.Equal(Ago(5) - TimeSpan.FromTicks(1), state!.CheckpointUtc);
        Assert.Equal(a.ItemId, Assert.Single(state.Delivered).ItemId);
    }

    [Fact]
    public async Task ManyCategories_AllShowUpWhenOneIsHuge()
    {
        _h.Config.MaxEntriesPerDigest = 6;
        var user = await _h.AddUserAsync("Ana", "ana@example.com");
        _h.Discovery.Give(user, Enumerable.Range(1, 40).Select(i => RunnerHarness.Movie("Movie " + i, Ago(3))).ToArray());
        _h.Discovery.Give(user, RunnerHarness.Movie("Only Doc", Ago(3), MediaCategory.Documentaries));

        await _h.RunAsync();

        Assert.Contains("Only Doc", _h.Transport.Sent.Single().Email.TextBody, StringComparison.Ordinal);
    }

    // Failure handling

    [Fact]
    public async Task TransientFailureThenSuccess_IsRetriedAndRecordedOnce()
    {
        var user = await _h.AddUserAsync("Ana", "ana@example.com");
        _h.Discovery.Give(user, RunnerHarness.Movie("Heat", Ago(2)));
        _h.Transport.Behavior = (_, attempt) => attempt == 1 ? Result(EmailTransportOutcome.Timeout) : Result(EmailTransportOutcome.Accepted);

        var summary = await _h.RunAsync();

        Assert.Equal(2, _h.Transport.Sent.Count);
        Assert.Equal(1, summary.MessagesAccepted);
        Assert.Equal(DigestRunOutcome.Completed, summary.Outcome);
        Assert.Equal(Upper, (await _h.State.GetAsync(user, CancellationToken.None))!.CheckpointUtc);
    }

    [Fact]
    public async Task TransientFailureEveryTime_StopsAfterTheBoundedAttempts_AndAdvancesNothing()
    {
        var failing = await _h.AddUserAsync("Ana", "ana@example.com");
        var fine = await _h.AddUserAsync("Bob", "bob@example.com");
        _h.Discovery.Give(failing, RunnerHarness.Movie("Heat", Ago(2)));
        _h.Discovery.Give(fine, RunnerHarness.Movie("Heat", Ago(2)));
        _h.Transport.Behavior = (email, _) => email.RecipientAddress == "ana@example.com" ? Result(EmailTransportOutcome.ConnectionFailed) : Result(EmailTransportOutcome.Accepted);

        var summary = await _h.RunAsync();

        Assert.Equal(3, _h.Transport.Attempts("ana@example.com"));
        Assert.Equal(1, _h.Transport.Attempts("bob@example.com"));
        Assert.Null(await _h.State.GetAsync(failing, CancellationToken.None));
        Assert.Equal(Upper, (await _h.State.GetAsync(fine, CancellationToken.None))!.CheckpointUtc);
        Assert.Equal(DigestRunOutcome.CompletedWithFailures, summary.Outcome);
        Assert.True(summary.NeedsAttention);
        Assert.Equal(1, summary.MessagesFailed);
        Assert.Equal(1, summary.MessagesAccepted);
    }

    [Fact]
    public async Task PermanentRecipientRejection_IsNotRetried_AndOthersContinue()
    {
        var bad = await _h.AddUserAsync("Ana", "ana@example.com");
        var good = await _h.AddUserAsync("Bob", "bob@example.com");
        _h.Discovery.Give(bad, RunnerHarness.Movie("Heat", Ago(2)));
        _h.Discovery.Give(good, RunnerHarness.Movie("Heat", Ago(2)));
        _h.Transport.Behavior = (email, _) => email.RecipientAddress == "ana@example.com" ? Result(EmailTransportOutcome.RecipientRejected, 550) : Result(EmailTransportOutcome.Accepted);

        var summary = await _h.RunAsync();

        Assert.Equal(1, _h.Transport.Attempts("ana@example.com"));
        Assert.Equal(1, summary.MessagesAccepted);
        Assert.Equal(DigestRunOutcome.CompletedWithFailures, summary.Outcome);
        Assert.Null(await _h.State.GetAsync(bad, CancellationToken.None));
    }

    [Fact]
    public async Task TemporaryRecipientReply_4xx_IsRetried()
    {
        var user = await _h.AddUserAsync("Ana", "ana@example.com");
        _h.Discovery.Give(user, RunnerHarness.Movie("Heat", Ago(2)));
        _h.Transport.Behavior = (_, attempt) => attempt == 1 ? Result(EmailTransportOutcome.RecipientRejected, 451) : Result(EmailTransportOutcome.Accepted);

        await _h.RunAsync();

        Assert.Equal(2, _h.Transport.Sent.Count);
    }

    [Theory]
    [InlineData(EmailTransportOutcome.AuthenticationFailed)]
    [InlineData(EmailTransportOutcome.TlsFailed)]
    public async Task LoginOrTlsFailure_StopsTheWholeRun_SoOtherUsersAreNotHammered(EmailTransportOutcome outcome)
    {
        var first = await _h.AddUserAsync("Ana", "ana@example.com");
        var second = await _h.AddUserAsync("Bob", "bob@example.com");
        var third = await _h.AddUserAsync("Cai", "cai@example.com");
        foreach (var u in new[] { first, second, third })
        {
            _h.Discovery.Give(u, RunnerHarness.Movie("Heat", Ago(2)));
        }

        _h.Transport.Behavior = (_, _) => Result(outcome);

        var summary = await _h.RunAsync();

        Assert.Single(_h.Transport.Sent);
        Assert.Equal(DigestRunOutcome.Aborted, summary.Outcome);
        Assert.Equal("smtp-unusable", summary.Reason);
        Assert.Equal(2, summary.UsersNotAttempted);
        Assert.Empty(await _h.State.GetAllAsync(CancellationToken.None));
    }

    [Fact]
    public async Task LostSmtpReply_IsUncertain_NotRetriedNow_NotRecorded_AndResentNextRunWithTheSameId()
    {
        var user = await _h.AddUserAsync("Ana", "ana@example.com");
        _h.Discovery.Give(user, RunnerHarness.Movie("Heat", Ago(2)));
        _h.Transport.Behavior = (_, _) => Result(EmailTransportOutcome.Uncertain);

        var first = await _h.RunAsync();

        Assert.Single(_h.Transport.Sent);
        Assert.Equal(1, first.MessagesUncertain);
        Assert.Equal(DigestRunOutcome.CompletedWithFailures, first.Outcome);
        Assert.Null(await _h.State.GetAsync(user, CancellationToken.None));

        // The documented trade-off: nothing was confirmed, so a later run resends (a duplicate is possible).
        // Run again at the same moment: the same digest then carries the same message id.
        _h.Transport.Behavior = (_, _) => Result(EmailTransportOutcome.Accepted);
        await _h.RunAsync();

        Assert.Equal(2, _h.Transport.Sent.Count);
        Assert.NotNull(_h.Transport.Sent[0].Email.MessageIdLocalPart);
        Assert.Equal(_h.Transport.Sent[0].Email.MessageIdLocalPart, _h.Transport.Sent[1].Email.MessageIdLocalPart);
        Assert.Equal(Upper, (await _h.State.GetAsync(user, CancellationToken.None))!.CheckpointUtc);
    }

    [Fact]
    public async Task UserFailureAtDiscovery_LeavesTheirCheckpoint_AndDoesNotLookLikeASuccessfulEmptyDigest()
    {
        var broken = await _h.AddUserAsync("Ana", "ana@example.com");
        var fine = await _h.AddUserAsync("Bob", "bob@example.com");
        _h.Discovery.FailFor(broken);
        _h.Discovery.Give(fine, RunnerHarness.Movie("Heat", Ago(2)));

        var summary = await _h.RunAsync();

        Assert.Null(await _h.State.GetAsync(broken, CancellationToken.None));
        Assert.Equal(0, summary.UsersWithNoNewMedia);
        Assert.Equal(1, summary.MessagesFailed);
        Assert.Equal(DigestRunOutcome.CompletedWithFailures, summary.Outcome);
        Assert.Equal(1, summary.MessagesAccepted);
    }

    [Fact]
    public async Task RenderFailure_SendsNothing_AndAdvancesNothing()
    {
        var user = await _h.AddUserAsync("Ana", "ana@example.com");
        _h.Discovery.Give(user, RunnerHarness.Movie("Heat", Ago(2)));
        var renderer = new Mock<IDigestRenderer>();
        renderer.Setup(r => r.Render(It.IsAny<DigestModel>(), It.IsAny<DigestRecipient>())).Throws(new DigestRenderException("x", new InvalidOperationException()));
        _h.Renderer = renderer.Object;

        var summary = await _h.RunAsync();

        Assert.Empty(_h.Transport.Sent);
        Assert.Null(await _h.State.GetAsync(user, CancellationToken.None));
        Assert.Equal(DigestRunOutcome.CompletedWithFailures, summary.Outcome);
    }

    [Fact]
    public async Task CheckpointWriteFailure_AfterAcceptance_IsReportedStopsTheRun_AndKeepsThePreviousState()
    {
        var first = await _h.AddUserAsync("Ana", "ana@example.com");
        var second = await _h.AddUserAsync("Bob", "bob@example.com");
        _h.Discovery.Give(first, RunnerHarness.Movie("Heat", Ago(2)));
        _h.Discovery.Give(second, RunnerHarness.Movie("Heat", Ago(2)));
        var inner = _h.InnerState;
        var oldCheckpoint = Ago(30);
        await inner.SaveAsync(new UserDeliveryState { UserId = first, CheckpointUtc = oldCheckpoint }, CancellationToken.None);
        _h.State = new RunnerHarness.FailingState(inner) { FailWhen = _ => true };

        var summary = await _h.RunAsync();

        Assert.Single(_h.Transport.Sent);
        Assert.Equal(1, summary.AcceptedButNotRecorded);
        Assert.Equal(DigestRunOutcome.Aborted, summary.Outcome);
        Assert.Equal("state-write-failed", summary.Reason);
        Assert.Equal(1, summary.UsersNotAttempted);
        Assert.Equal(oldCheckpoint, (await inner.GetAsync(first, CancellationToken.None))!.CheckpointUtc);
    }

    [Fact]
    public async Task OneUsersCheckpointNeverMovesAnothers()
    {
        var advanced = await _h.AddUserAsync("Ana", "ana@example.com");
        var failed = await _h.AddUserAsync("Bob", "bob@example.com");
        var bobBefore = Ago(48);
        await _h.InnerState.SaveAsync(new UserDeliveryState { UserId = failed, CheckpointUtc = bobBefore }, CancellationToken.None);
        _h.Discovery.Give(advanced, RunnerHarness.Movie("Heat", Ago(2)));
        _h.Discovery.Give(failed, RunnerHarness.Movie("Heat", Ago(2)));
        _h.Transport.Behavior = (email, _) => email.RecipientAddress == "bob@example.com" ? Result(EmailTransportOutcome.Timeout) : Result(EmailTransportOutcome.Accepted);

        await _h.RunAsync();

        Assert.Equal(Upper, (await _h.State.GetAsync(advanced, CancellationToken.None))!.CheckpointUtc);
        Assert.Equal(bobBefore, (await _h.State.GetAsync(failed, CancellationToken.None))!.CheckpointUtc);
    }

    // Cancellation, overlap, restart

    [Fact]
    public async Task Cancellation_StopsTheRun_KeepsEarlierUsersAndLeavesTheCurrentOneUntouched()
    {
        var first = await _h.AddUserAsync("Ana", "ana@example.com");
        var second = await _h.AddUserAsync("Bob", "bob@example.com");
        var order = new[] { first, second }.OrderBy(u => u).ToList();
        foreach (var u in order)
        {
            _h.Discovery.Give(u, RunnerHarness.Movie("Heat", Ago(2)));
        }

        using var cts = new CancellationTokenSource();
        var firstEmail = order[0] == first ? "ana@example.com" : "bob@example.com";
        _h.Transport.BeforeAnswer = async (email, _) =>
        {
            if (email.RecipientAddress != firstEmail)
            {
                await cts.CancelAsync();
            }
        };
        _h.Transport.Behavior = (email, _) => email.RecipientAddress == firstEmail ? Result(EmailTransportOutcome.Accepted) : Result(EmailTransportOutcome.Cancelled);

        var summary = await _h.RunAsync(cts.Token);

        Assert.Equal(DigestRunOutcome.Cancelled, summary.Outcome);
        Assert.Equal(Upper, (await _h.State.GetAsync(order[0], CancellationToken.None))!.CheckpointUtc);
        Assert.Null(await _h.State.GetAsync(order[1], CancellationToken.None));
    }

    [Fact]
    public async Task CancelledBeforeStart_DoesNothing()
    {
        var user = await _h.AddUserAsync("Ana", "ana@example.com");
        _h.Discovery.Give(user, RunnerHarness.Movie("Heat", Ago(2)));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var summary = await _h.RunAsync(cts.Token);

        Assert.Equal(DigestRunOutcome.Cancelled, summary.Outcome);
        Assert.Empty(_h.Transport.Sent);
    }

    [Fact]
    public async Task OverlappingRuns_SecondDoesNothing_AndTheLockIsReleasedAfterwards()
    {
        var user = await _h.AddUserAsync("Ana", "ana@example.com");
        _h.Discovery.Give(user, RunnerHarness.Movie("Heat", Ago(2)));
        var entered = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        _h.Transport.BeforeAnswer = async (_, _) =>
        {
            entered.TrySetResult();
            await release.Task;
        };

        var firstRun = _h.RunAsync();
        await entered.Task;
        var second = await _h.RunAsync();
        release.SetResult();
        var first = await firstRun;
        var third = await _h.RunAsync();

        Assert.Equal(DigestRunOutcome.Skipped, second.Outcome);
        Assert.Equal("already-running", second.Reason);
        Assert.Equal(DigestRunOutcome.Completed, first.Outcome);
        Assert.Equal(DigestRunOutcome.Completed, third.Outcome);
        Assert.Single(_h.Transport.Sent);
    }

    [Fact]
    public async Task StaleLockFileFromACrash_DoesNotBlockTheNextRun()
    {
        await File.WriteAllTextAsync(Path.Combine(_h.Root, "digest-run.lock"), "left behind by a crashed process");
        var user = await _h.AddUserAsync("Ana", "ana@example.com");
        _h.Discovery.Give(user, RunnerHarness.Movie("Heat", Ago(2)));

        var summary = await _h.RunAsync();

        Assert.Equal(DigestRunOutcome.Completed, summary.Outcome);
        Assert.Single(_h.Transport.Sent);
    }

    [Fact]
    public async Task RestartRecovery_StateSurvivesANewProcess_SoNothingIsResentAndNothingIsSkipped()
    {
        var user = await _h.AddUserAsync("Ana", "ana@example.com");
        _h.Discovery.Give(user, RunnerHarness.Movie("Heat", Ago(2)));
        await _h.RunAsync();

        // A "restart": fresh store and runner objects over the same folder.
        _h.State = new DeliveryStateStore(_h.Root, _h.Time);
        _h.Time.Advance(TimeSpan.FromHours(2));
        _h.Discovery.Give(user, RunnerHarness.Movie("Newer", Upper.AddHours(1)));
        await _h.RunAsync();

        Assert.Equal(2, _h.Transport.Sent.Count);
        Assert.DoesNotContain("Heat", _h.Transport.Sent[1].Email.TextBody, StringComparison.Ordinal);
        Assert.Contains("Newer", _h.Transport.Sent[1].Email.TextBody, StringComparison.Ordinal);
    }

    // Configuration and libraries

    [Fact]
    public async Task DeletedLibrary_AbortsTheRunBeforeAnythingIsDiscoveredSentOrAdvanced()
    {
        var user = await _h.AddUserAsync("Ana", "ana@example.com");
        _h.Discovery.Give(user, RunnerHarness.Movie("Heat", Ago(2)));
        _h.Validator.Setup(v => v.Validate(It.IsAny<System.Collections.Generic.IReadOnlyList<MonitoredLibrary>>()))
            .Returns(new[] { new MonitoredLibraryIssue(0, RunnerHarness.Library, MonitoredLibraryIssueKind.NotFound) });

        var summary = await _h.RunAsync();

        Assert.Equal(DigestRunOutcome.Aborted, summary.Outcome);
        Assert.Equal("libraries-invalid", summary.Reason);
        Assert.Empty(_h.Discovery.Calls);
        Assert.Empty(_h.Transport.Sent);
        Assert.Empty(await _h.State.GetAllAsync(CancellationToken.None));
    }

    [Fact]
    public async Task LibrariesThatCannotBeVerified_AbortInsteadOfRunningUnchecked()
    {
        await _h.AddUserAsync("Ana", "ana@example.com");
        _h.Validator.Setup(v => v.Validate(It.IsAny<System.Collections.Generic.IReadOnlyList<MonitoredLibrary>>())).Throws(new InvalidOperationException("db"));

        var summary = await _h.RunAsync();

        Assert.Equal("libraries-unverifiable", summary.Reason);
        Assert.Empty(_h.Discovery.Calls);
    }

    [Fact]
    public async Task DuplicateLibraryEntry_IsOnlyAWarning_NotAnAbort()
    {
        var user = await _h.AddUserAsync("Ana", "ana@example.com");
        _h.Discovery.Give(user, RunnerHarness.Movie("Heat", Ago(2)));
        _h.Validator.Setup(v => v.Validate(It.IsAny<System.Collections.Generic.IReadOnlyList<MonitoredLibrary>>()))
            .Returns(new[] { new MonitoredLibraryIssue(1, RunnerHarness.Library, MonitoredLibraryIssueKind.Duplicate) });

        var summary = await _h.RunAsync();

        Assert.Equal(DigestRunOutcome.Completed, summary.Outcome);
    }

    [Fact]
    public async Task NoMonitoredLibraries_MeansNothingIsDone_NotEverythingIsMonitored()
    {
        _h.Config.MonitoredLibraries.Clear();
        var user = await _h.AddUserAsync("Ana", "ana@example.com");
        _h.Discovery.Give(user, RunnerHarness.Movie("Heat", Ago(2)));

        var summary = await _h.RunAsync();

        Assert.Equal(DigestRunOutcome.Skipped, summary.Outcome);
        Assert.Equal("no-libraries", summary.Reason);
        Assert.Empty(_h.Discovery.Calls);
    }

    [Fact]
    public async Task InvalidConfiguration_AbortsAndDoesNothing()
    {
        await _h.AddUserAsync("Ana", "ana@example.com");
        _h.Config.MaxEntriesPerDigest = 0;

        var summary = await _h.RunAsync();

        Assert.Equal("configuration-invalid", summary.Reason);
        Assert.Empty(_h.Discovery.Calls);
    }

    [Fact]
    public async Task DamagedStateFile_AbortsBeforeSending_AndIsLeftAlone()
    {
        var user = await _h.AddUserAsync("Ana", "ana@example.com");
        _h.Discovery.Give(user, RunnerHarness.Movie("Heat", Ago(2)));
        var path = Path.Combine(_h.Root, "delivery-state.json");
        await File.WriteAllTextAsync(path, "{ damaged");

        var summary = await _h.RunAsync();

        Assert.Equal("state-unreadable", summary.Reason);
        Assert.Empty(_h.Transport.Sent);
        Assert.Equal("{ damaged", await File.ReadAllTextAsync(path));
    }

    // Safeguards

    [Fact]
    public async Task Disabled_ExitsSafelyWithoutTouchingAnything()
    {
        _h.Config.DigestEnabled = false;
        var user = await _h.AddUserAsync("Ana", "ana@example.com");
        _h.Discovery.Give(user, RunnerHarness.Movie("Heat", Ago(2)));

        var summary = await _h.RunAsync();

        Assert.Equal(DigestRunOutcome.Skipped, summary.Outcome);
        Assert.Equal(DigestRunMode.Disabled, summary.Mode);
        Assert.Empty(_h.Discovery.Calls);
        Assert.Empty(_h.Transport.Sent);
        Assert.Null(await _h.Summaries.GetAsync(CancellationToken.None));
    }

    [Fact]
    public async Task DryRun_BuildsAndRendersButSendsNothingAndRecordsNothing()
    {
        _h.Config.DigestDryRun = true;
        _h.Config.SmtpEnabled = false;
        var user = await _h.AddUserAsync("Ana", "ana@example.com");
        _h.Discovery.Give(user, RunnerHarness.Movie("Heat", Ago(2)));

        var summary = await _h.RunAsync();

        Assert.Equal(DigestRunMode.DryRun, summary.Mode);
        Assert.Equal(1, summary.DigestsRenderedDryRun);
        Assert.Equal(0, summary.MessagesAccepted);
        Assert.Empty(_h.Transport.Sent);
        Assert.Empty(await _h.State.GetAllAsync(CancellationToken.None));
    }

    [Fact]
    public async Task DryRun_NeverRecordsABaselineEither()
    {
        _h.Config.DigestDryRun = true;
        _h.Config.FallbackLookbackHours = 0;
        await _h.AddUserAsync("Ana", "ana@example.com");

        await _h.RunAsync();

        Assert.Empty(await _h.State.GetAllAsync(CancellationToken.None));
    }

    [Fact]
    public async Task LiveRun_WithSmtpDisabled_Aborts()
    {
        _h.Config.SmtpEnabled = false;
        await _h.AddUserAsync("Ana", "ana@example.com");

        var summary = await _h.RunAsync();

        Assert.Equal("smtp-disabled", summary.Reason);
        Assert.Empty(_h.Discovery.Calls);
    }

    [Fact]
    public async Task LiveRun_WithUsernameButNoPassword_Aborts()
    {
        _h.Config.SmtpUsername = "mailer";
        await _h.AddUserAsync("Ana", "ana@example.com");

        var summary = await _h.RunAsync();

        Assert.Equal("smtp-password-missing", summary.Reason);
        Assert.Empty(_h.Transport.Sent);
    }

    [Fact]
    public async Task LiveRun_UsesTheStoredPasswordWithoutLoggingIt()
    {
        _h.Config.SmtpUsername = "mailer-account";
        await _h.Passwords.SetAsync("Pa55-secret-value", CancellationToken.None);
        var user = await _h.AddUserAsync("Ana", "ana@example.com");
        _h.Discovery.Give(user, RunnerHarness.Movie("Heat", Ago(2)));

        await _h.RunAsync();

        Assert.Equal("Pa55-secret-value", _h.Transport.Sent.Single().Settings.Password);
        Assert.All(_h.Log.Lines, line =>
        {
            Assert.DoesNotContain("Pa55-secret-value", line, StringComparison.Ordinal);
            Assert.DoesNotContain("ana@example.com", line, StringComparison.Ordinal);
            Assert.DoesNotContain("Heat", line, StringComparison.Ordinal);
            Assert.DoesNotContain("mailer-account", line, StringComparison.Ordinal);
        });
    }

    // Baselines and history

    [Fact]
    public async Task NewUser_GetsOnlyTheBoundedLookback_NotTheWholeLibrary()
    {
        _h.Config.FallbackLookbackHours = 24;
        var user = await _h.AddUserAsync("Ana", "ana@example.com");
        _h.Discovery.Give(user, RunnerHarness.Movie("Ancient", Ago(24 * 20)), RunnerHarness.Movie("Recent", Ago(3)));

        await _h.RunAsync();

        var body = _h.Transport.Sent.Single().Email.TextBody;
        Assert.Contains("Recent", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Ancient", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NewUser_WithZeroLookback_GetsNoEmailNowButOnlyFutureMedia()
    {
        _h.Config.FallbackLookbackHours = 0;
        var user = await _h.AddUserAsync("Ana", "ana@example.com");
        _h.Discovery.Give(user, RunnerHarness.Movie("Old", Ago(5)));

        var first = await _h.RunAsync();
        Assert.Empty(_h.Transport.Sent);
        Assert.Equal(1, first.BaselinesRecorded);
        Assert.Equal(Upper, (await _h.State.GetAsync(user, CancellationToken.None))!.CheckpointUtc);

        _h.Time.Advance(TimeSpan.FromHours(6));
        _h.Discovery.Give(user, RunnerHarness.Movie("Fresh", Upper.AddHours(1)));
        await _h.RunAsync();

        var body = _h.Transport.Sent.Single().Email.TextBody;
        Assert.Contains("Fresh", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Old", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task LegacyCheckpoint_IsUsedAsTheStartForAUserWithNoState()
    {
        await File.WriteAllTextAsync(Path.Combine(_h.Root, "checkpoint.json"), JsonSerializer.Serialize(new { LastSuccessfulRunUtc = Ago(10) }));
        var user = await _h.AddUserAsync("Ana", "ana@example.com");
        _h.Discovery.Give(user, RunnerHarness.Movie("Before", Ago(12)), RunnerHarness.Movie("After", Ago(8)));

        await _h.RunAsync();

        var body = _h.Transport.Sent.Single().Email.TextBody;
        Assert.Contains("After", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Before", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PausedUser_KeepsTheirPendingHistory_UntilNotificationsAreBackOn()
    {
        var user = await _h.AddUserAsync("Ana", "ana@example.com");
        _h.Discovery.Give(user, RunnerHarness.Movie("Heat", Ago(2)));
        await _h.RunAsync();
        var checkpoint = (await _h.State.GetAsync(user, CancellationToken.None))!.CheckpointUtc;

        await _h.Preferences.SaveAsync(new UserNotificationPreferences { EmailVerifiedUtc = RunnerHarness.Verified, UserId = user, Email = "ana@example.com", Language = "en", NotificationsEnabled = false }, CancellationToken.None);
        _h.Time.Advance(TimeSpan.FromDays(3));
        _h.Discovery.Give(user, RunnerHarness.Movie("While paused", Upper.AddDays(1)));
        await _h.RunAsync();
        Assert.Equal(checkpoint, (await _h.State.GetAsync(user, CancellationToken.None))!.CheckpointUtc);

        await _h.Preferences.SaveAsync(new UserNotificationPreferences { EmailVerifiedUtc = RunnerHarness.Verified, UserId = user, Email = "ana@example.com", Language = "en", NotificationsEnabled = true }, CancellationToken.None);
        _h.Time.Advance(TimeSpan.FromDays(1));
        await _h.RunAsync();

        Assert.Contains("While paused", _h.Transport.Sent[1].Email.TextBody, StringComparison.Ordinal);
    }

    // Reporting

    [Fact]
    public async Task Summary_IsPersistedWithCountsOnly_NoPersonalData()
    {
        var user = await _h.AddUserAsync("Private Person", "private.person@example.com");
        _h.Discovery.Give(user, RunnerHarness.Movie("Confidential Title", Ago(2)));

        await _h.RunAsync();

        var saved = await _h.Summaries.GetAsync(CancellationToken.None);
        Assert.NotNull(saved);
        Assert.Equal(1, saved!.MessagesAccepted);
        var raw = await File.ReadAllTextAsync(Path.Combine(_h.Root, "last-run.json"));
        Assert.DoesNotContain("Private", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("example.com", raw, StringComparison.Ordinal);
        Assert.DoesNotContain("Confidential", raw, StringComparison.Ordinal);
        Assert.DoesNotContain(user.ToString(), raw, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PartialFailure_IsVisibleInTheSummaryAndTheLog_NotReportedAsSuccess()
    {
        var a = await _h.AddUserAsync("Ana", "ana@example.com");
        var b = await _h.AddUserAsync("Bob", "bob@example.com");
        _h.Discovery.Give(a, RunnerHarness.Movie("Heat", Ago(2)));
        _h.Discovery.Give(b, RunnerHarness.Movie("Heat", Ago(2)));
        _h.Transport.Behavior = (email, _) => email.RecipientAddress == "bob@example.com" ? Result(EmailTransportOutcome.RecipientRejected, 550) : Result(EmailTransportOutcome.Accepted);

        var summary = await _h.RunAsync();

        Assert.Equal(DigestRunOutcome.CompletedWithFailures, summary.Outcome);
        Assert.True(summary.NeedsAttention);
        Assert.Contains(_h.Log.Lines, l => l.Contains("CompletedWithFailures", StringComparison.Ordinal));
        Assert.Equal(DigestRunOutcome.CompletedWithFailures, (await _h.Summaries.GetAsync(CancellationToken.None))!.Outcome);
    }

    [Fact]
    public async Task TheRun_NeverChangesTheConfiguration()
    {
        var user = await _h.AddUserAsync("Ana", "ana@example.com");
        _h.Discovery.Give(user, RunnerHarness.Movie("Heat", Ago(2)));

        // FixedConfig throws if Save is ever called.
        var summary = await _h.RunAsync();

        Assert.Equal(DigestRunOutcome.Completed, summary.Outcome);
    }
}
