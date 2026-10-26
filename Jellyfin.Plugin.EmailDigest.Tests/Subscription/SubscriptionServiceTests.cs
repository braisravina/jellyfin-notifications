using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.EmailDigest.Configuration;
using Jellyfin.Plugin.EmailDigest.Delivery;
using Jellyfin.Plugin.EmailDigest.Email;
using Jellyfin.Plugin.EmailDigest.Models;
using Jellyfin.Plugin.EmailDigest.Services;
using Jellyfin.Plugin.EmailDigest.Subscription;
using Jellyfin.Plugin.EmailDigest.Tests.Delivery;
using Jellyfin.Plugin.EmailDigest.Verification;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Jellyfin.Plugin.EmailDigest.Tests.Subscription;

public sealed class SubscriptionServiceTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "EmailDigestSub_" + Guid.NewGuid());
    private readonly RunnerHarness.ManualTime _time = new(Start);
    private readonly PluginConfiguration _config;
    private readonly UserPreferenceRepository _preferences;
    private readonly DeliveryStatusStore _status;
    private readonly AddressVerificationService _verification;
    private readonly UnsubscribeTokenService _tokens;
    private readonly RunnerHarness.FakeUsers _users = new();
    private readonly RunnerHarness.FakeTransport _transport = new();
    private readonly FakeSchedule _schedule = new();
    private readonly SubscriptionService _service;

    public SubscriptionServiceTests()
    {
        Directory.CreateDirectory(_folder);
        _config = new PluginConfiguration
        {
            DigestEnabled = true,
            DigestDryRun = false,
            SmtpEnabled = true,
            SmtpHost = "smtp.example.com",
            SenderEmailAddress = "digest@example.com",
            SenderDisplayName = "Home Jellyfin",
            PublicBaseUrl = "https://jellyfin.example.org/jf/",
            DefaultLanguage = "en"
        };
        _preferences = new UserPreferenceRepository(_folder);
        _status = new DeliveryStatusStore(_folder);
        _verification = new AddressVerificationService(_folder, () => VerificationPolicy.ForLifetime(_config.VerificationLinkMinutes), _time, NullLogger.Instance);
        _tokens = new UnsubscribeTokenService(_folder);
        _service = new SubscriptionService(
            _preferences,
            _verification,
            _status,
            _schedule,
            new RunnerHarness.FixedConfig(_config),
            new SmtpPasswordStore(_folder, () => null),
            _transport,
            new VerificationMessageBuilder(new LocalizationService()),
            _tokens,
            _users,
            _time,
            NullLogger<SubscriptionService>.Instance);
    }

    private string PreferencesPath => Path.Combine(_folder, "user-preferences.json");

    public void Dispose()
    {
        _preferences.Dispose();
        _status.Dispose();
        _verification.Dispose();
        Directory.Delete(_folder, recursive: true);
    }

    private Guid NewUser(string name = "Ana", bool disabled = false)
    {
        var id = Guid.NewGuid();
        _users.Add(id, new UserInfo(name, disabled));
        return id;
    }

    private async Task<Guid> VerifiedUserAsync(string address, string language = "en", bool enabled = true)
    {
        var id = NewUser();
        await _preferences.UpdateAsync(
            id,
            _ => new UserNotificationPreferences { UserId = id, Email = address, EmailVerifiedUtc = Start.AddDays(-1), Language = language, NotificationsEnabled = enabled },
            CancellationToken.None);
        return id;
    }

    private static string TokenIn(OutgoingEmail email)
    {
        var marker = "token=";
        var start = email.TextBody.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        var end = email.TextBody.IndexOfAny(['\r', '\n'], start);
        return email.TextBody[start..end];
    }

    // Overview

    [Fact]
    public async Task Overview_UnknownOrDisabledUser_IsNull()
    {
        Assert.Null(await _service.GetOverviewAsync(Guid.NewGuid(), CancellationToken.None));
        Assert.Null(await _service.GetOverviewAsync(NewUser(disabled: true), CancellationToken.None));
        Assert.Null(await _service.GetOverviewAsync(Guid.Empty, CancellationToken.None));
    }

    [Fact]
    public async Task Overview_NewUser_HasSafeDefaults()
    {
        var overview = (await _service.GetOverviewAsync(NewUser(), CancellationToken.None))!;

        Assert.False(overview.NotificationsEnabled);
        Assert.Equal("en", overview.Language);
        Assert.Equal(string.Empty, overview.Address);
        Assert.False(overview.AddressVerified);
        Assert.False(overview.WillReceiveDigests);
        Assert.Null(overview.PendingAddress);
        Assert.Null(overview.LastAcceptedUtc);
        Assert.True(overview.VerificationAvailable);
    }

    [Fact]
    public async Task Overview_ShowsOnlyTheUsersOwnInformation()
    {
        var ana = await VerifiedUserAsync("ana@example.com", "gl");
        await VerifiedUserAsync("bob@example.com");
        await _status.RecordAttemptAsync(ana, DeliveryAttemptStatus.Accepted, Start.AddHours(-3), CancellationToken.None);
        _schedule.Next = Start.AddHours(6);

        var overview = (await _service.GetOverviewAsync(ana, CancellationToken.None))!;

        Assert.Equal("ana@example.com", overview.Address);
        Assert.True(overview.AddressVerified);
        Assert.Equal("gl", overview.Language);
        Assert.True(overview.WillReceiveDigests);
        Assert.Equal(Start.AddHours(-3), overview.LastAcceptedUtc);
        Assert.Equal(DeliveryAttemptStatus.Accepted, overview.LastAttemptStatus);
        Assert.Equal(Start.AddHours(6), overview.NextRunUtc);
        Assert.DoesNotContain("bob@example.com", overview.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Overview_NextRun_IsHiddenWhileTheDigestIsOff()
    {
        var user = await VerifiedUserAsync("ana@example.com");
        _schedule.Next = Start.AddHours(6);
        _config.DigestEnabled = false;

        var overview = (await _service.GetOverviewAsync(user, CancellationToken.None))!;

        Assert.Null(overview.NextRunUtc);
        Assert.False(overview.DigestsActive);
        Assert.False(overview.WillReceiveDigests);
    }

    [Theory]
    [InlineData(true, false, true, true)]
    [InlineData(true, true, true, false)]
    [InlineData(false, false, true, false)]
    [InlineData(true, false, false, false)]
    public async Task Overview_WillReceiveDigests_NeedsRealSendingToBeOn(bool digestEnabled, bool dryRun, bool smtpEnabled, bool expected)
    {
        var user = await VerifiedUserAsync("ana@example.com");
        _config.DigestEnabled = digestEnabled;
        _config.DigestDryRun = dryRun;
        _config.SmtpEnabled = smtpEnabled;

        Assert.Equal(expected, (await _service.GetOverviewAsync(user, CancellationToken.None))!.WillReceiveDigests);
    }

    [Fact]
    public async Task Overview_LegacyUnverifiedAddress_IsShownAsUnverified_AndNeverReceives()
    {
        var user = NewUser();
        await File.WriteAllTextAsync(PreferencesPath, "[{\"UserId\":\"" + user + "\",\"Email\":\"old@example.com\",\"Language\":\"es\",\"NotificationsEnabled\":true}]");

        var overview = (await _service.GetOverviewAsync(user, CancellationToken.None))!;

        Assert.Equal("old@example.com", overview.Address);
        Assert.False(overview.AddressVerified);
        Assert.True(overview.NotificationsEnabled);
        Assert.False(overview.WillReceiveDigests);
    }

    [Fact]
    public async Task Overview_VerificationUnavailable_WhenSmtpOffOrNoPublicAddress()
    {
        var user = NewUser();
        _config.PublicBaseUrl = string.Empty;
        Assert.False((await _service.GetOverviewAsync(user, CancellationToken.None))!.VerificationAvailable);
        _config.PublicBaseUrl = "https://x.example";
        _config.SmtpEnabled = false;
        Assert.False((await _service.GetOverviewAsync(user, CancellationToken.None))!.VerificationAvailable);
    }

    // Notifications and language

    [Fact]
    public async Task SetNotifications_TogglesOnlyTheFlag()
    {
        var user = await VerifiedUserAsync("ana@example.com", "gl", enabled: true);

        Assert.Equal(SubscriptionChange.Saved, await _service.SetNotificationsAsync(user, false, CancellationToken.None));

        var stored = (await _preferences.GetAsync(user, CancellationToken.None))!;
        Assert.False(stored.NotificationsEnabled);
        Assert.Equal("ana@example.com", stored.Email);
        Assert.NotNull(stored.EmailVerifiedUtc);
        Assert.Equal("gl", stored.Language);
    }

    [Fact]
    public async Task SetNotifications_WithoutAnAddress_IsAllowedButNothingIsSent()
    {
        var user = NewUser();

        Assert.Equal(SubscriptionChange.Saved, await _service.SetNotificationsAsync(user, true, CancellationToken.None));

        var overview = (await _service.GetOverviewAsync(user, CancellationToken.None))!;
        Assert.True(overview.NotificationsEnabled);
        Assert.False(overview.WillReceiveDigests);
    }

    [Fact]
    public async Task SetNotifications_ForAnUnavailableUser_ChangesNothing()
    {
        Assert.Equal(SubscriptionChange.UserUnavailable, await _service.SetNotificationsAsync(Guid.NewGuid(), true, CancellationToken.None));
        Assert.Equal(SubscriptionChange.UserUnavailable, await _service.SetNotificationsAsync(NewUser(disabled: true), true, CancellationToken.None));
        Assert.Empty(await _preferences.GetAllAsync(CancellationToken.None));
    }

    [Fact]
    public async Task SetNotifications_NeverTouchesAnotherUser()
    {
        var ana = await VerifiedUserAsync("ana@example.com");
        var bob = await VerifiedUserAsync("bob@example.com");

        await _service.SetNotificationsAsync(ana, false, CancellationToken.None);

        Assert.True((await _preferences.GetAsync(bob, CancellationToken.None))!.NotificationsEnabled);
    }

    [Theory]
    [InlineData("es", "es")]
    [InlineData("GL", "gl")]
    public async Task SetLanguage_AcceptsTheThreeLanguages(string asked, string stored)
    {
        var user = NewUser();

        Assert.Equal(SubscriptionChange.Saved, await _service.SetLanguageAsync(user, asked, CancellationToken.None));
        Assert.Equal(stored, (await _preferences.GetAsync(user, CancellationToken.None))!.Language);
    }

    [Theory]
    [InlineData("fr")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("en\r\nBcc: x")]
    public async Task SetLanguage_RejectsAnythingElse_AndChangesNothing(string? asked)
    {
        var user = await VerifiedUserAsync("ana@example.com", "gl");

        Assert.Equal(SubscriptionChange.InvalidValue, await _service.SetLanguageAsync(user, asked, CancellationToken.None));
        Assert.Equal("gl", (await _preferences.GetAsync(user, CancellationToken.None))!.Language);
    }

    [Fact]
    public async Task SetNotifications_WithADamagedFile_ReportsStorageError_AndLeavesTheFileAlone()
    {
        var user = NewUser();
        await File.WriteAllTextAsync(PreferencesPath, "{ broken");

        Assert.Equal(SubscriptionChange.StorageError, await _service.SetNotificationsAsync(user, true, CancellationToken.None));
        Assert.Equal("{ broken", await File.ReadAllTextAsync(PreferencesPath));
    }

    // Address request

    [Fact]
    public async Task Request_SendsOneEmail_ToTheNewAddressOnly_WithALinkFromTheConfiguredBase()
    {
        var user = await VerifiedUserAsync("old@example.com", "es");

        var result = await _service.RequestAddressAsync(user, "new@example.com", CancellationToken.None);

        Assert.Equal(AddressRequestStatus.Sent, result.Status);
        Assert.Equal(Start.AddMinutes(60), result.ExpiresUtc);
        var (_, email) = Assert.Single(_transport.Sent);
        Assert.Equal("new@example.com", email.RecipientAddress);
        Assert.Contains("https://jellyfin.example.org/jf/EmailDigest/Verify?token=", email.TextBody, StringComparison.Ordinal);
        Assert.Equal("Confirma tu dirección de correo", email.Subject);
        Assert.Contains("Home Jellyfin", email.TextBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Request_DoesNotChangeTheAddressInUse_UntilConfirmed()
    {
        var user = await VerifiedUserAsync("old@example.com");

        await _service.RequestAddressAsync(user, "new@example.com", CancellationToken.None);

        var stored = (await _preferences.GetAsync(user, CancellationToken.None))!;
        Assert.Equal("old@example.com", stored.Email);
        Assert.True(stored.CanReceiveDigest());
        var overview = (await _service.GetOverviewAsync(user, CancellationToken.None))!;
        Assert.Equal("new@example.com", overview.PendingAddress);
        Assert.Equal("old@example.com", overview.Address);
    }

    [Fact]
    public async Task Request_Email_ContainsNothingPrivate()
    {
        var user = await VerifiedUserAsync("old@example.com");
        await VerifiedUserAsync("bob@example.com");

        await _service.RequestAddressAsync(user, "new@example.com", CancellationToken.None);

        var email = _transport.Sent.Single().Email;
        var all = email.Subject + email.TextBody + email.HtmlBody;
        Assert.DoesNotContain("bob@example.com", all, StringComparison.Ordinal);
        Assert.DoesNotContain("old@example.com", all, StringComparison.Ordinal);
        Assert.DoesNotContain("Ana", all, StringComparison.Ordinal);
        Assert.DoesNotContain(user.ToString(), all, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("")]
    [InlineData("nope")]
    [InlineData("a@example.com, b@example.com")]
    [InlineData("a@example.com\r\nBcc: v@example.com")]
    public async Task Request_InvalidAddress_SendsNothing(string address)
    {
        var user = NewUser();

        Assert.Equal(AddressRequestStatus.InvalidAddress, (await _service.RequestAddressAsync(user, address, CancellationToken.None)).Status);
        Assert.Empty(_transport.Sent);
    }

    [Fact]
    public async Task Request_WhenSmtpOffOrNoPublicAddress_IsNotAvailable_AndSendsNothing()
    {
        var user = NewUser();
        _config.PublicBaseUrl = string.Empty;
        Assert.Equal(AddressRequestStatus.NotAvailable, (await _service.RequestAddressAsync(user, "a@example.com", CancellationToken.None)).Status);

        _config.PublicBaseUrl = "https://x.example";
        _config.SmtpEnabled = false;
        Assert.Equal(AddressRequestStatus.NotAvailable, (await _service.RequestAddressAsync(user, "a@example.com", CancellationToken.None)).Status);
        Assert.Empty(_transport.Sent);
    }

    [Fact]
    public async Task Request_ForTheAlreadyVerifiedAddress_SendsNothing_RegardlessOfCase()
    {
        var user = await VerifiedUserAsync("ana@example.com");

        Assert.Equal(AddressRequestStatus.AlreadyVerified, (await _service.RequestAddressAsync(user, "ANA@example.com", CancellationToken.None)).Status);
        Assert.Empty(_transport.Sent);
    }

    [Fact]
    public async Task Request_ForAnUnverifiedLegacyAddress_DoesSendAVerification()
    {
        var user = NewUser();
        await File.WriteAllTextAsync(PreferencesPath, "[{\"UserId\":\"" + user + "\",\"Email\":\"old@example.com\",\"Language\":\"en\",\"NotificationsEnabled\":true}]");

        Assert.Equal(AddressRequestStatus.Sent, (await _service.RequestAddressAsync(user, "old@example.com", CancellationToken.None)).Status);
    }

    [Fact]
    public async Task Request_ForAnUnavailableUser_SendsNothing()
    {
        Assert.Equal(AddressRequestStatus.UserUnavailable, (await _service.RequestAddressAsync(Guid.NewGuid(), "a@example.com", CancellationToken.None)).Status);
        Assert.Equal(AddressRequestStatus.UserUnavailable, (await _service.RequestAddressAsync(NewUser(disabled: true), "a@example.com", CancellationToken.None)).Status);
        Assert.Empty(_transport.Sent);
    }

    [Fact]
    public async Task Request_Repeated_IsRateLimited_AndSendsNoSecondEmail()
    {
        var user = NewUser();
        await _service.RequestAddressAsync(user, "a@example.com", CancellationToken.None);

        var second = await _service.RequestAddressAsync(user, "a@example.com", CancellationToken.None);

        Assert.Equal(AddressRequestStatus.RateLimited, second.Status);
        Assert.Equal(RateLimitKind.Cooldown, second.RateLimit);
        Assert.Equal(TimeSpan.FromMinutes(1), second.RetryAfter);
        Assert.Single(_transport.Sent);
    }

    [Fact]
    public async Task Request_SendFailure_WithdrawsTheLink()
    {
        var user = NewUser();
        _transport.Behavior = (_, _) => new EmailTransportResult(EmailTransportOutcome.ConnectionFailed);

        var result = await _service.RequestAddressAsync(user, "a@example.com", CancellationToken.None);

        Assert.Equal(AddressRequestStatus.SendFailed, result.Status);
        Assert.Null((await _service.GetOverviewAsync(user, CancellationToken.None))!.PendingAddress);
        Assert.Equal(ConfirmOutcome.Invalid, await _service.ConfirmAddressAsync(TokenIn(_transport.Sent.Single().Email), CancellationToken.None));
    }

    [Fact]
    public async Task Request_UncertainSend_KeepsTheLinkInCaseItArrives()
    {
        var user = NewUser();
        _transport.Behavior = (_, _) => new EmailTransportResult(EmailTransportOutcome.Uncertain);

        var result = await _service.RequestAddressAsync(user, "a@example.com", CancellationToken.None);

        Assert.Equal(AddressRequestStatus.SentUncertain, result.Status);
        Assert.Equal("a@example.com", (await _service.GetOverviewAsync(user, CancellationToken.None))!.PendingAddress);
    }

    [Fact]
    public async Task Request_UsesTheUsersLanguage_ElseTheDefault()
    {
        var gl = await VerifiedUserAsync("gl@example.com", "gl");
        await _service.RequestAddressAsync(gl, "new@example.com", CancellationToken.None);
        Assert.Equal("Confirma o teu enderezo de correo", _transport.Sent.Last().Email.Subject);

        _config.DefaultLanguage = "es";
        var fresh = NewUser();
        await _service.RequestAddressAsync(fresh, "fresh@example.com", CancellationToken.None);
        Assert.Equal("Confirma tu dirección de correo", _transport.Sent.Last().Email.Subject);
    }

    [Fact]
    public async Task Cancel_WithdrawsTheRequest_AndOnlyTheCallersOwn()
    {
        var ana = NewUser();
        var bob = NewUser("Bob");
        await _service.RequestAddressAsync(ana, "ana@example.com", CancellationToken.None);
        await _service.RequestAddressAsync(bob, "bob@example.com", CancellationToken.None);

        Assert.True(await _service.CancelPendingAddressAsync(ana, CancellationToken.None));

        Assert.Null((await _service.GetOverviewAsync(ana, CancellationToken.None))!.PendingAddress);
        Assert.Equal("bob@example.com", (await _service.GetOverviewAsync(bob, CancellationToken.None))!.PendingAddress);
        Assert.False(await _service.CancelPendingAddressAsync(ana, CancellationToken.None));
    }

    // Confirmation

    [Fact]
    public async Task Confirm_AppliesTheAddress_AsVerified_KeepingOtherPreferences()
    {
        var user = await VerifiedUserAsync("old@example.com", "gl", enabled: true);
        await _service.RequestAddressAsync(user, "new@example.com", CancellationToken.None);
        _time.Advance(TimeSpan.FromMinutes(5));

        var outcome = await _service.ConfirmAddressAsync(TokenIn(_transport.Sent.Single().Email), CancellationToken.None);

        Assert.Equal(ConfirmOutcome.Confirmed, outcome);
        var stored = (await _preferences.GetAsync(user, CancellationToken.None))!;
        Assert.Equal("new@example.com", stored.Email);
        Assert.Equal(_time.GetUtcNow(), stored.EmailVerifiedUtc);
        Assert.Equal("gl", stored.Language);
        Assert.True(stored.NotificationsEnabled);
        Assert.Null((await _service.GetOverviewAsync(user, CancellationToken.None))!.PendingAddress);
    }

    [Fact]
    public async Task Confirm_ForANewUser_CreatesTheRecord_WithNotificationsStillOff()
    {
        var user = NewUser();
        await _service.RequestAddressAsync(user, "a@example.com", CancellationToken.None);

        await _service.ConfirmAddressAsync(TokenIn(_transport.Sent.Single().Email), CancellationToken.None);

        var stored = (await _preferences.GetAsync(user, CancellationToken.None))!;
        Assert.Equal("a@example.com", stored.Email);
        Assert.False(stored.NotificationsEnabled);
        Assert.False(stored.CanReceiveDigest());
    }

    [Fact]
    public async Task Confirm_Replay_IsRefused()
    {
        var user = NewUser();
        await _service.RequestAddressAsync(user, "a@example.com", CancellationToken.None);
        var token = TokenIn(_transport.Sent.Single().Email);

        Assert.Equal(ConfirmOutcome.Confirmed, await _service.ConfirmAddressAsync(token, CancellationToken.None));
        Assert.Equal(ConfirmOutcome.Invalid, await _service.ConfirmAddressAsync(token, CancellationToken.None));
    }

    [Fact]
    public async Task Confirm_Expired_IsRefused_AndChangesNothing()
    {
        var user = await VerifiedUserAsync("old@example.com");
        await _service.RequestAddressAsync(user, "new@example.com", CancellationToken.None);
        _time.Advance(TimeSpan.FromMinutes(61));

        Assert.Equal(ConfirmOutcome.Invalid, await _service.ConfirmAddressAsync(TokenIn(_transport.Sent.Single().Email), CancellationToken.None));
        Assert.Equal("old@example.com", (await _preferences.GetAsync(user, CancellationToken.None))!.Email);
    }

    [Fact]
    public async Task Confirm_AnOlderLink_AfterANewRequest_IsRefused()
    {
        var user = NewUser();
        await _service.RequestAddressAsync(user, "first@example.com", CancellationToken.None);
        _time.Advance(TimeSpan.FromMinutes(2));
        await _service.RequestAddressAsync(user, "second@example.com", CancellationToken.None);

        Assert.Equal(ConfirmOutcome.Invalid, await _service.ConfirmAddressAsync(TokenIn(_transport.Sent[0].Email), CancellationToken.None));
        Assert.Equal(ConfirmOutcome.Confirmed, await _service.ConfirmAddressAsync(TokenIn(_transport.Sent[1].Email), CancellationToken.None));
        Assert.Equal("second@example.com", (await _preferences.GetAsync(user, CancellationToken.None))!.Email);
    }

    [Fact]
    public async Task Confirm_AfterTheUserWasDisabled_AppliesNothing()
    {
        var user = NewUser();
        await _service.RequestAddressAsync(user, "a@example.com", CancellationToken.None);
        _users.Add(user, new UserInfo("Ana", true));

        Assert.Equal(ConfirmOutcome.NotApplied, await _service.ConfirmAddressAsync(TokenIn(_transport.Sent.Single().Email), CancellationToken.None));
        Assert.Null(await _preferences.GetAsync(user, CancellationToken.None));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("garbage")]
    public async Task Confirm_BadToken_IsInvalid(string? token)
    {
        Assert.Equal(ConfirmOutcome.Invalid, await _service.ConfirmAddressAsync(token, CancellationToken.None));
    }

    [Fact]
    public async Task Confirm_OneUsersLink_NeverChangesAnotherUsersAddress()
    {
        var ana = await VerifiedUserAsync("ana@example.com");
        var bob = await VerifiedUserAsync("bob@example.com");
        await _service.RequestAddressAsync(ana, "ana2@example.com", CancellationToken.None);

        await _service.ConfirmAddressAsync(TokenIn(_transport.Sent.Single().Email), CancellationToken.None);

        Assert.Equal("ana2@example.com", (await _preferences.GetAsync(ana, CancellationToken.None))!.Email);
        Assert.Equal("bob@example.com", (await _preferences.GetAsync(bob, CancellationToken.None))!.Email);
    }

    [Fact]
    public async Task ConcurrentRequestsAndConfirmations_ByDifferentUsers_AllSucceedIndependently()
    {
        var users = Enumerable.Range(0, 10).Select(i => (Id: NewUser("U" + i), Address: $"u{i}@example.com")).ToList();

        await Task.WhenAll(users.Select(u => _service.RequestAddressAsync(u.Id, u.Address, CancellationToken.None)));
        var confirmations = await Task.WhenAll(_transport.Sent.Select(s => _service.ConfirmAddressAsync(TokenIn(s.Email), CancellationToken.None)));

        Assert.All(confirmations, c => Assert.Equal(ConfirmOutcome.Confirmed, c));
        foreach (var (id, address) in users)
        {
            Assert.Equal(address, (await _preferences.GetAsync(id, CancellationToken.None))!.Email);
        }
    }

    // Unsubscribe

    [Fact]
    public async Task Unsubscribe_TurnsOffOnlyThatUsersNotifications()
    {
        var ana = await VerifiedUserAsync("ana@example.com", "gl");
        var bob = await VerifiedUserAsync("bob@example.com");

        Assert.True(await _service.UnsubscribeAsync(_tokens.Create(ana), CancellationToken.None));

        var stored = (await _preferences.GetAsync(ana, CancellationToken.None))!;
        Assert.False(stored.NotificationsEnabled);
        Assert.Equal("ana@example.com", stored.Email);
        Assert.NotNull(stored.EmailVerifiedUtc);
        Assert.Equal("gl", stored.Language);
        Assert.True((await _preferences.GetAsync(bob, CancellationToken.None))!.NotificationsEnabled);
    }

    [Fact]
    public async Task Unsubscribe_IsIdempotent_AndCreatesNoRecordForSomeoneWithNone()
    {
        var user = NewUser();
        var token = _tokens.Create(user);

        Assert.True(await _service.UnsubscribeAsync(token, CancellationToken.None));
        Assert.True(await _service.UnsubscribeAsync(token, CancellationToken.None));
        Assert.False(File.Exists(PreferencesPath));
    }

    [Fact]
    public async Task Unsubscribe_InvalidOrForgedToken_ChangesNothing()
    {
        var ana = await VerifiedUserAsync("ana@example.com");

        Assert.False(await _service.UnsubscribeAsync("garbage", CancellationToken.None));
        Assert.False(await _service.UnsubscribeAsync(null, CancellationToken.None));
        Assert.False(await _service.UnsubscribeAsync(_tokens.Create(ana) + "x", CancellationToken.None));
        Assert.True((await _preferences.GetAsync(ana, CancellationToken.None))!.NotificationsEnabled);
    }

    [Fact]
    public async Task Unsubscribe_ForADeletedOrDisabledAccount_StillWorks_BecauseItOnlyTurnsThingsOff()
    {
        var user = await VerifiedUserAsync("ana@example.com");
        _users.Add(user, new UserInfo("Ana", true));

        Assert.True(await _service.UnsubscribeAsync(_tokens.Create(user), CancellationToken.None));
        Assert.False((await _preferences.GetAsync(user, CancellationToken.None))!.NotificationsEnabled);
    }

    [Fact]
    public async Task Unsubscribe_KeepsDeliveryStateAndStatus_ItIsNotAccountDeletion()
    {
        var user = await VerifiedUserAsync("ana@example.com");
        var state = new DeliveryStateStore(_folder, _time);
        await state.SaveAsync(new UserDeliveryState { UserId = user, CheckpointUtc = Start.AddDays(-1) }, CancellationToken.None);
        await _status.RecordAttemptAsync(user, DeliveryAttemptStatus.Accepted, Start.AddDays(-1), CancellationToken.None);

        await _service.UnsubscribeAsync(_tokens.Create(user), CancellationToken.None);

        Assert.NotNull(await state.GetAsync(user, CancellationToken.None));
        Assert.NotNull(await _status.GetAsync(user, CancellationToken.None));
        Assert.NotNull(await _preferences.GetAsync(user, CancellationToken.None));
        state.Dispose();
    }

    [Fact]
    public async Task Unsubscribe_WithADamagedFile_ReportsFailure_AndLeavesTheFileAlone()
    {
        var user = NewUser();
        await File.WriteAllTextAsync(PreferencesPath, "{ broken");

        Assert.False(await _service.UnsubscribeAsync(_tokens.Create(user), CancellationToken.None));
        Assert.Equal("{ broken", await File.ReadAllTextAsync(PreferencesPath));
    }

    [Fact]
    public async Task Unsubscribe_ThenResubscribe_ThroughThePage_Works()
    {
        var user = await VerifiedUserAsync("ana@example.com");
        await _service.UnsubscribeAsync(_tokens.Create(user), CancellationToken.None);

        await _service.SetNotificationsAsync(user, true, CancellationToken.None);

        Assert.True((await _preferences.GetAsync(user, CancellationToken.None))!.CanReceiveDigest());
    }

    private sealed class FakeSchedule : IScheduleInfo
    {
        public DateTimeOffset? Next { get; set; }

        public DateTimeOffset? GetNextRunUtc() => Next;
    }
}
