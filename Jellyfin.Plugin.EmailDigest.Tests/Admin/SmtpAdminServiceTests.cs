using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.EmailDigest.Admin;
using Jellyfin.Plugin.EmailDigest.Configuration;
using Jellyfin.Plugin.EmailDigest.Email;
using Jellyfin.Plugin.EmailDigest.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.EmailDigest.Tests.Admin;

public sealed class SmtpAdminServiceTests : IDisposable
{
    private const string Secret = "Tr0ub4dor&3-SECRET";

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "emaildigest-admin-" + Guid.NewGuid().ToString("N"));
    private readonly FakeConfigurationAccessor _configuration = new();
    private readonly Mock<IEmailTransport> _transport = new();
    private readonly SmtpPasswordStore _passwords;
    private readonly ManualTimeProvider _time = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
    private string? _external;

    public SmtpAdminServiceTests()
    {
        Directory.CreateDirectory(_directory);
        _passwords = new SmtpPasswordStore(_directory, () => _external);
        _transport
            .Setup(t => t.TestConnectionAsync(It.IsAny<SmtpSettings>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmailTransportResult(EmailTransportOutcome.Accepted));
        _transport
            .Setup(t => t.SendAsync(It.IsAny<SmtpSettings>(), It.IsAny<OutgoingEmail>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmailTransportResult(EmailTransportOutcome.Accepted));
    }

    public void Dispose()
    {
        _passwords.Dispose();
        Directory.Delete(_directory, recursive: true);
    }

    private SmtpAdminService Service()
    {
        return new SmtpAdminService(_configuration, _passwords, _transport.Object, new LocalizationService(), _time, NullLogger<SmtpAdminService>.Instance);
    }

    private static SmtpSettingsUpdate ValidUpdate(string security = "StartTls", string username = "mailer")
    {
        return new SmtpSettingsUpdate(true, "smtp.example.com", 587, security, username, "digest@example.com", "Jellyfin", 30);
    }

    private static string Json(AdminResponse response)
    {
        return JsonSerializer.Serialize(response.Body);
    }

    // Settings

    [Fact]
    public void GetSettings_ReturnsSettingsAndPasswordStatusOnly()
    {
        _configuration.Current.SmtpHost = "smtp.example.com";

        var response = Service().GetSettings();

        var dto = Assert.IsType<SmtpSettingsDto>(response.Body);
        Assert.Equal("smtp.example.com", dto.Host);
        Assert.Equal(new SmtpPasswordStatusDto(false, "None"), dto.Password);
    }

    [Fact]
    public async Task GetSettings_AfterSettingAPassword_ShowsOnlyThatItIsSet()
    {
        var service = Service();
        await service.SetPasswordAsync(new SmtpPasswordUpdate(Secret), CancellationToken.None);

        var response = service.GetSettings();

        Assert.Equal(new SmtpPasswordStatusDto(true, "Stored"), ((SmtpSettingsDto)response.Body!).Password);
        Assert.DoesNotContain(Secret, Json(response), StringComparison.Ordinal);
        Assert.DoesNotContain("assword\":\"", Json(response), StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsDto_HasNoStringPropertyThatCouldCarryAPassword()
    {
        // The only member named Password is the status object, never a string.
        Assert.Equal(typeof(SmtpPasswordStatusDto), typeof(SmtpSettingsDto).GetProperty("Password")!.PropertyType);
        Assert.DoesNotContain(typeof(SmtpSettingsUpdate).GetProperties(), p => p.Name.Contains("assword", StringComparison.Ordinal));
    }

    [Fact]
    public void UpdateSettings_Valid_SavesOnlySmtpFieldsAndKeepsEverythingElse()
    {
        _configuration.Current.DigestEnabled = true;
        _configuration.Current.DefaultLanguage = "gl";
        _configuration.Current.FallbackLookbackHours = 24;
        _configuration.Current.MonitoredLibraries.Add(new MonitoredLibrary { LibraryId = Guid.NewGuid(), Category = MediaCategory.Anime });

        var response = Service().UpdateSettings(ValidUpdate());

        Assert.Equal(200, response.StatusCode);
        Assert.Equal("smtp.example.com", _configuration.Current.SmtpHost);
        Assert.Equal(SmtpSecurityMode.StartTls, _configuration.Current.SmtpSecurity);
        Assert.True(_configuration.Current.DigestEnabled);
        Assert.Equal("gl", _configuration.Current.DefaultLanguage);
        Assert.Equal(24, _configuration.Current.FallbackLookbackHours);
        Assert.Single(_configuration.Current.MonitoredLibraries);
    }

    [Fact]
    public void UpdateSettings_Invalid_IsRejectedAndNothingChanges()
    {
        var before = _configuration.Current.SmtpHost;

        var response = Service().UpdateSettings(ValidUpdate() with { Port = 70000 });

        Assert.Equal(400, response.StatusCode);
        Assert.Contains("SmtpPort", Json(response), StringComparison.Ordinal);
        Assert.Equal(before, _configuration.Current.SmtpHost);
        Assert.Equal(0, _configuration.SaveCount);
    }

    [Fact]
    public void UpdateSettings_UnencryptedWithUsername_IsRejected()
    {
        var response = Service().UpdateSettings(ValidUpdate("None", "mailer"));

        Assert.Equal(400, response.StatusCode);
        Assert.Equal(0, _configuration.SaveCount);
    }

    [Fact]
    public void UpdateSettings_UnencryptedWithoutUsername_IsAllowedWhenChosenExplicitly()
    {
        var response = Service().UpdateSettings(ValidUpdate("None", string.Empty));

        Assert.Equal(200, response.StatusCode);
        Assert.Equal(SmtpSecurityMode.None, _configuration.Current.SmtpSecurity);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Auto")]
    [InlineData("99")]
    public void UpdateSettings_UnknownSecurity_IsRejected(string? security)
    {
        Assert.Equal(400, Service().UpdateSettings(ValidUpdate() with { Security = security }).StatusCode);
    }

    [Fact]
    public void UpdateSettings_NullRequest_IsRejected()
    {
        Assert.Equal(400, Service().UpdateSettings(null).StatusCode);
    }

    [Fact]
    public async Task UpdateSettings_NeverTouchesTheStoredPassword()
    {
        await _passwords.SetAsync(Secret, CancellationToken.None);

        Service().UpdateSettings(ValidUpdate());

        Assert.Equal(Secret, await _passwords.ReadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task UpdateSettings_ChangingTheHost_ReportsThatTheStoredPasswordWasRemoved()
    {
        // Stands in for Plugin.UpdateConfiguration, which applies the same policy before saving.
        _configuration.OnSave = (current, next) =>
        {
            if (ConfigurationChangePolicy.MustClearStoredPassword(current, next))
            {
                _passwords.ClearAsync(CancellationToken.None).GetAwaiter().GetResult();
            }
        };
        var service = Service();
        service.UpdateSettings(ValidUpdate());
        await _passwords.SetAsync(Secret, CancellationToken.None);

        var response = service.UpdateSettings(ValidUpdate() with { Host = "other.example.com" });

        var dto = Assert.IsType<SmtpSettingsDto>(response.Body);
        Assert.True(dto.PasswordCleared);
        Assert.False(dto.Password.IsSet);
    }

    [Fact]
    public async Task UpdateSettings_WithoutServerChange_DoesNotReportPasswordCleared()
    {
        var service = Service();
        service.UpdateSettings(ValidUpdate());
        await _passwords.SetAsync(Secret, CancellationToken.None);

        var dto = (SmtpSettingsDto)service.UpdateSettings(ValidUpdate() with { SenderName = "Other" }).Body!;

        Assert.False(dto.PasswordCleared);
        Assert.True(dto.Password.IsSet);
    }

    // Password lifecycle

    [Fact]
    public async Task Password_SetReplaceClear_Lifecycle()
    {
        var service = Service();

        var set = await service.SetPasswordAsync(new SmtpPasswordUpdate("first-pass"), CancellationToken.None);
        Assert.Equal(new SmtpPasswordStatusDto(true, "Stored"), set.Body);

        await service.SetPasswordAsync(new SmtpPasswordUpdate("second-pass"), CancellationToken.None);
        Assert.Equal("second-pass", await _passwords.ReadAsync(CancellationToken.None));

        var cleared = await service.ClearPasswordAsync(CancellationToken.None);
        Assert.Equal(new SmtpPasswordStatusDto(false, "None"), cleared.Body);
        Assert.Null(await _passwords.ReadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Password_LeavingItUnchanged_MeansNotCallingTheEndpoint()
    {
        var service = Service();
        await service.SetPasswordAsync(new SmtpPasswordUpdate(Secret), CancellationToken.None);

        service.UpdateSettings(ValidUpdate());
        service.GetSettings();

        Assert.Equal(Secret, await _passwords.ReadAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("two\nlines")]
    public async Task Password_Invalid_Returns400WithoutEchoingIt(string? value)
    {
        var response = await Service().SetPasswordAsync(new SmtpPasswordUpdate(value), CancellationToken.None);

        Assert.Equal(400, response.StatusCode);
        Assert.DoesNotContain("two", Json(response), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Password_NullRequest_IsRejected()
    {
        Assert.Equal(400, (await Service().SetPasswordAsync(null, CancellationToken.None)).StatusCode);
    }

    [Fact]
    public async Task Password_ManagedExternally_CannotBeChangedOrClearedFromTheWeb()
    {
        var secretFile = Path.Combine(_directory, "docker-secret");
        await File.WriteAllTextAsync(secretFile, "docker-pass");
        _external = secretFile;
        var service = Service();

        var set = await service.SetPasswordAsync(new SmtpPasswordUpdate("override"), CancellationToken.None);
        var clear = await service.ClearPasswordAsync(CancellationToken.None);
        var settings = service.GetSettings();

        Assert.Equal(409, set.StatusCode);
        Assert.Equal(409, clear.StatusCode);
        Assert.Equal(new SmtpPasswordStatusDto(true, "External"), ((SmtpSettingsDto)settings.Body!).Password);
        Assert.DoesNotContain("docker-pass", Json(settings), StringComparison.Ordinal);
        Assert.Equal("docker-pass", await _passwords.ReadAsync(CancellationToken.None));
    }

    [Fact]
    public void PasswordUpdate_ToStringIsRedacted()
    {
        Assert.DoesNotContain(Secret, new SmtpPasswordUpdate(Secret).ToString(), StringComparison.Ordinal);
    }

    // Tests of connection / email

    [Fact]
    public async Task TestConnection_UsesSavedSettingsAndStoredPasswordAndSendsNoMail()
    {
        _configuration.Current.SmtpHost = "smtp.example.com";
        _configuration.Current.SmtpPort = 465;
        _configuration.Current.SmtpSecurity = SmtpSecurityMode.ImplicitTls;
        _configuration.Current.SmtpUsername = "mailer";
        await _passwords.SetAsync(Secret, CancellationToken.None);
        SmtpSettings? seen = null;
        _transport
            .Setup(t => t.TestConnectionAsync(It.IsAny<SmtpSettings>(), It.IsAny<CancellationToken>()))
            .Callback<SmtpSettings, CancellationToken>((s, _) => seen = s)
            .ReturnsAsync(new EmailTransportResult(EmailTransportOutcome.Accepted));

        var response = await Service().TestConnectionAsync("en", CancellationToken.None);

        Assert.Equal("smtp.example.com", seen!.Host);
        Assert.Equal(Secret, seen.Password);
        Assert.Equal("Accepted", ((TransportResultDto)response.Body!).Outcome);
        _transport.Verify(t => t.SendAsync(It.IsAny<SmtpSettings>(), It.IsAny<OutgoingEmail>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.DoesNotContain(Secret, Json(response), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(EmailTransportOutcome.InvalidRequest, "configuration")]
    [InlineData(EmailTransportOutcome.ConnectionFailed, "connectivity")]
    [InlineData(EmailTransportOutcome.Timeout, "connectivity")]
    [InlineData(EmailTransportOutcome.TlsFailed, "tls")]
    [InlineData(EmailTransportOutcome.AuthenticationFailed, "authentication")]
    [InlineData(EmailTransportOutcome.RecipientRejected, "recipient")]
    [InlineData(EmailTransportOutcome.UnexpectedError, "other")]
    public async Task TestConnection_EachFailureHasADistinctCategoryAndLocalizedMessage(EmailTransportOutcome outcome, string category)
    {
        _transport
            .Setup(t => t.TestConnectionAsync(It.IsAny<SmtpSettings>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmailTransportResult(outcome, 550));

        var dto = (TransportResultDto)(await Service().TestConnectionAsync("es", CancellationToken.None)).Body!;

        Assert.Equal(category, dto.Category);
        Assert.Equal(outcome.ToString(), dto.Outcome);
        Assert.Equal(550, dto.SmtpStatusCode);
        Assert.False(string.IsNullOrWhiteSpace(dto.Message));
        Assert.DoesNotContain("AdminOutcome", dto.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TestEmail_SendsExactlyOneMessageToTheNamedAddress()
    {
        OutgoingEmail? sent = null;
        _transport
            .Setup(t => t.SendAsync(It.IsAny<SmtpSettings>(), It.IsAny<OutgoingEmail>(), It.IsAny<CancellationToken>()))
            .Callback<SmtpSettings, OutgoingEmail, CancellationToken>((_, e, _) => sent = e)
            .ReturnsAsync(new EmailTransportResult(EmailTransportOutcome.Accepted));

        var response = await Service().SendTestEmailAsync(new TestEmailRequest("  admin@example.com "), "gl", CancellationToken.None);

        Assert.Equal(200, response.StatusCode);
        Assert.Equal("admin@example.com", sent!.RecipientAddress);
        Assert.Contains("proba", sent.Subject, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", sent.HtmlBody, StringComparison.OrdinalIgnoreCase);
        _transport.Verify(t => t.SendAsync(It.IsAny<SmtpSettings>(), It.IsAny<OutgoingEmail>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-an-address")]
    [InlineData("a@b.co\r\nBcc: x@y.zz")]
    public async Task TestEmail_InvalidDestination_IsRejectedAndNothingIsSent(string? recipient)
    {
        var response = await Service().SendTestEmailAsync(new TestEmailRequest(recipient), "en", CancellationToken.None);

        Assert.Equal(400, response.StatusCode);
        _transport.Verify(t => t.SendAsync(It.IsAny<SmtpSettings>(), It.IsAny<OutgoingEmail>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TestEmail_NullRequest_IsRejected()
    {
        Assert.Equal(400, (await Service().SendTestEmailAsync(null, "en", CancellationToken.None)).StatusCode);
    }

    [Fact]
    public async Task TestEmail_RepeatedQuickly_IsThrottled_ThenAllowedAgain()
    {
        var service = Service();
        var request = new TestEmailRequest("admin@example.com");

        Assert.Equal(200, (await service.SendTestEmailAsync(request, "en", CancellationToken.None)).StatusCode);
        Assert.Equal(429, (await service.SendTestEmailAsync(request, "en", CancellationToken.None)).StatusCode);
        _time.Advance(TimeSpan.FromSeconds(6));
        Assert.Equal(200, (await service.SendTestEmailAsync(request, "en", CancellationToken.None)).StatusCode);

        _transport.Verify(t => t.SendAsync(It.IsAny<SmtpSettings>(), It.IsAny<OutgoingEmail>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task TestEmail_InvalidAddressDoesNotConsumeTheThrottle()
    {
        var service = Service();

        await service.SendTestEmailAsync(new TestEmailRequest("bad"), "en", CancellationToken.None);

        Assert.Equal(200, (await service.SendTestEmailAsync(new TestEmailRequest("ok@example.com"), "en", CancellationToken.None)).StatusCode);
    }

    // Strings

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("gl")]
    public void Strings_AreTranslatedAndComplete(string language)
    {
        var response = Service().GetStrings(language);

        var json = JsonSerializer.SerializeToElement(response.Body);
        Assert.Equal(language, json.GetProperty("language").GetString());
        var strings = json.GetProperty("strings");
        foreach (var key in AdminStringKeys.All)
        {
            var value = strings.GetProperty(key).GetString();
            Assert.False(string.IsNullOrWhiteSpace(value));
            Assert.NotEqual(key, value);
        }
    }

    [Fact]
    public void Strings_UnsupportedLanguage_FallsBackToEnglish()
    {
        var json = JsonSerializer.SerializeToElement(Service().GetStrings("fr").Body);

        Assert.Equal("en", json.GetProperty("language").GetString());
    }

    [Fact]
    public void Strings_EveryTransportOutcomeHasAMessageKey()
    {
        foreach (var outcome in Enum.GetValues<EmailTransportOutcome>())
        {
            Assert.Contains("AdminOutcome" + outcome, AdminStringKeys.All);
        }
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _now;

        public ManualTimeProvider(DateTimeOffset now)
        {
            _now = now;
        }

        public override DateTimeOffset GetUtcNow()
        {
            return _now;
        }

        public void Advance(TimeSpan by)
        {
            _now += by;
        }
    }

    private sealed class FakeConfigurationAccessor : ISmtpConfigurationAccessor
    {
        public PluginConfiguration Current { get; private set; } = new();

        public int SaveCount { get; private set; }

        public Action<PluginConfiguration, PluginConfiguration>? OnSave { get; set; }

        public PluginConfiguration GetCopy()
        {
            var copy = new PluginConfiguration
            {
                SmtpEnabled = Current.SmtpEnabled,
                SmtpHost = Current.SmtpHost,
                SmtpPort = Current.SmtpPort,
                SmtpSecurity = Current.SmtpSecurity,
                SmtpUsername = Current.SmtpUsername,
                SmtpTimeoutSeconds = Current.SmtpTimeoutSeconds,
                SenderEmailAddress = Current.SenderEmailAddress,
                SenderDisplayName = Current.SenderDisplayName,
                DigestEnabled = Current.DigestEnabled,
                DefaultLanguage = Current.DefaultLanguage,
                FallbackLookbackHours = Current.FallbackLookbackHours
            };
            foreach (var library in Current.MonitoredLibraries)
            {
                copy.MonitoredLibraries.Add(library);
            }

            return copy;
        }

        public void Save(PluginConfiguration configuration)
        {
            OnSave?.Invoke(Current, configuration);
            Current = configuration;
            SaveCount++;
        }
    }
}
