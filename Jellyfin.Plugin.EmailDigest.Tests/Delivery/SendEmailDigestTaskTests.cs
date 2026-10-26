using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.EmailDigest.Admin;
using Jellyfin.Plugin.EmailDigest.Delivery;
using Jellyfin.Plugin.EmailDigest.Email;
using Jellyfin.Plugin.EmailDigest.ScheduledTasks;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.EmailDigest.Tests.Delivery;

public sealed class SendEmailDigestTaskTests : IDisposable
{
    private static readonly DateTimeOffset Upper = RunnerHarness.Start - DigestRunner.SettleMargin;

    private readonly RunnerHarness _h = new();

    public void Dispose()
    {
        _h.Dispose();
    }

    private SendEmailDigestTask Task() => new(_h.NewRunner(), NullLogger<SendEmailDigestTask>.Instance);

    private sealed class Progress : IProgress<double>
    {
        public double Last { get; private set; } = -1;

        public void Report(double value)
        {
            Last = value;
        }
    }

    [Fact]
    public void Identity_IsUnchanged_AndItRunsDailyButTheRunnerDecidesWhetherToDoAnything()
    {
        var task = Task();

        Assert.Equal("EmailDigestSendTask", task.Key);
        Assert.Equal("Email Digest", task.Category);
        var trigger = Assert.Single(task.GetDefaultTriggers());
        Assert.Equal(TaskTriggerInfoType.DailyTrigger, trigger.Type);
        Assert.Contains("dry-run", task.Description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Disabled_CompletesQuietlyAndDoesNothing_ForScheduledAndManualRunsAlike()
    {
        _h.Config.DigestEnabled = false;
        var user = await _h.AddUserAsync("Ana", "ana@example.com");
        _h.Discovery.Give(user, RunnerHarness.Movie("Heat", Upper.AddHours(-2)));
        var progress = new Progress();

        await Task().ExecuteAsync(progress, CancellationToken.None);
        await Task().ExecuteAsync(progress, CancellationToken.None);

        Assert.Equal(100, progress.Last);
        Assert.Empty(_h.Transport.Sent);
        Assert.Empty(_h.Discovery.Calls);
    }

    [Fact]
    public async Task DryRun_SendsNothing_WhenRunFromTheTaskPage()
    {
        _h.Config.DigestDryRun = true;
        var user = await _h.AddUserAsync("Ana", "ana@example.com");
        _h.Discovery.Give(user, RunnerHarness.Movie("Heat", Upper.AddHours(-2)));

        await Task().ExecuteAsync(new Progress(), CancellationToken.None);

        Assert.Empty(_h.Transport.Sent);
        Assert.Empty(await _h.State.GetAllAsync(CancellationToken.None));
    }

    [Fact]
    public async Task LiveRun_SendsAndDoesNotThrow()
    {
        var user = await _h.AddUserAsync("Ana", "ana@example.com");
        _h.Discovery.Give(user, RunnerHarness.Movie("Heat", Upper.AddHours(-2)));

        await Task().ExecuteAsync(new Progress(), CancellationToken.None);

        Assert.Single(_h.Transport.Sent);
    }

    [Fact]
    public async Task PartialFailure_MakesTheTaskFail_WithCountsAndAReasonButNoPersonalData()
    {
        var good = await _h.AddUserAsync("Ana", "ana@example.com");
        var bad = await _h.AddUserAsync("Private Person", "private.person@example.com");
        _h.Discovery.Give(good, RunnerHarness.Movie("Confidential Title", Upper.AddHours(-2)));
        _h.Discovery.Give(bad, RunnerHarness.Movie("Confidential Title", Upper.AddHours(-2)));
        _h.Transport.Behavior = (email, _) => email.RecipientAddress.StartsWith("private", StringComparison.Ordinal)
            ? new EmailTransportResult(EmailTransportOutcome.RecipientRejected, 550)
            : new EmailTransportResult(EmailTransportOutcome.Accepted);

        var ex = await Assert.ThrowsAsync<DigestRunException>(() => Task().ExecuteAsync(new Progress(), CancellationToken.None));

        Assert.Contains("CompletedWithFailures", ex.Message, StringComparison.Ordinal);
        Assert.Contains("1 accepted, 1 failed", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Private", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("example.com", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Confidential", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AbortedRun_MakesTheTaskFail()
    {
        await _h.AddUserAsync("Ana", "ana@example.com");
        _h.Config.SmtpEnabled = false;

        var ex = await Assert.ThrowsAsync<DigestRunException>(() => Task().ExecuteAsync(new Progress(), CancellationToken.None));

        Assert.Contains("smtp-disabled", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cancellation_SurfacesAsACancellationNotAFailure()
    {
        await _h.AddUserAsync("Ana", "ana@example.com");
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Task().ExecuteAsync(new Progress(), cts.Token));
    }

    [Fact]
    public async Task OverlapWithAnotherRun_IsNotAFailure()
    {
        var user = await _h.AddUserAsync("Ana", "ana@example.com");
        _h.Discovery.Give(user, RunnerHarness.Movie("Heat", Upper.AddHours(-2)));
        var entered = new TaskCompletionSource();
        var release = new TaskCompletionSource();
        _h.Transport.BeforeAnswer = async (_, _) =>
        {
            entered.TrySetResult();
            await release.Task;
        };

        var running = Task().ExecuteAsync(new Progress(), CancellationToken.None);
        await entered.Task;
        await Task().ExecuteAsync(new Progress(), CancellationToken.None);
        release.SetResult();
        await running;

        Assert.Single(_h.Transport.Sent);
    }

    [Fact]
    public void Defaults_MeanNothingIsSentAfterInstallOrUpgrade()
    {
        var defaults = new Jellyfin.Plugin.EmailDigest.Configuration.PluginConfiguration();

        Assert.False(defaults.DigestEnabled);
        Assert.True(defaults.DigestDryRun);
        Assert.False(defaults.SmtpEnabled);
    }

    // Status for the administrator page

    [Fact]
    public async Task Status_NoRunYet_ReportsTheModeAndNoLastRun()
    {
        var service = new StatusAdminService(new RunnerHarness.FixedConfig(_h.Config), _h.Summaries, _h.Preferences);

        var dto = Assert.IsType<DigestStatusDto>((await service.GetStatusAsync(CancellationToken.None)).Body);

        Assert.Equal("Live", dto.Mode);
        Assert.Null(dto.LastRun);
    }

    [Theory]
    [InlineData(false, true, "Disabled")]
    [InlineData(true, true, "DryRun")]
    [InlineData(true, false, "Live")]
    public async Task Status_ModeFollowsTheSwitches(bool enabled, bool dryRun, string mode)
    {
        _h.Config.DigestEnabled = enabled;
        _h.Config.DigestDryRun = dryRun;
        var service = new StatusAdminService(new RunnerHarness.FixedConfig(_h.Config), _h.Summaries, _h.Preferences);

        var dto = (DigestStatusDto)(await service.GetStatusAsync(CancellationToken.None)).Body!;

        Assert.Equal(mode, dto.Mode);
    }

    [Fact]
    public async Task Status_ShowsTheLastRunAsCountsOnly()
    {
        var user = await _h.AddUserAsync("Private Person", "private.person@example.com");
        _h.Discovery.Give(user, RunnerHarness.Movie("Confidential Title", Upper.AddHours(-2)));
        await _h.RunAsync();
        var service = new StatusAdminService(new RunnerHarness.FixedConfig(_h.Config), _h.Summaries, _h.Preferences);

        var response = await service.GetStatusAsync(CancellationToken.None);

        var dto = (DigestStatusDto)response.Body!;
        Assert.Equal("Completed", dto.LastRun!.Outcome);
        Assert.Equal(1, dto.LastRun.MessagesAccepted);
        Assert.False(dto.LastRun.NeedsAttention);
        var json = System.Text.Json.JsonSerializer.Serialize(response.Body);
        Assert.DoesNotContain("Private", json, StringComparison.Ordinal);
        Assert.DoesNotContain("example.com", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Confidential", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Status_ReasonCodesAllHaveAdminStrings()
    {
        // Every reason a run can record must have wording, in every language, or the page would show blanks.
        var reasons = new[]
        {
            "disabled", "already-running", "configuration-unreadable", "configuration-invalid", "no-libraries", "libraries-invalid",
            "libraries-unverifiable", "smtp-disabled", "smtp-password-missing", "state-unreadable", "smtp-unusable",
            "state-write-failed", "user-failures", "cancelled", "unexpected-error"
        };
        var served = AdminStringKeys.All;

        foreach (var reason in reasons)
        {
            var key = "AdminReason" + string.Concat(reason.Split('-').Select(p => char.ToUpperInvariant(p[0]) + p[1..]));
            Assert.Contains(key, served);
        }

        foreach (var outcome in Enum.GetValues<DigestRunOutcome>())
        {
            Assert.Contains("AdminStatusOutcome" + outcome, served);
        }

        foreach (var mode in Enum.GetValues<DigestRunMode>())
        {
            Assert.Contains("AdminStatusMode" + mode, served);
        }
    }
}
