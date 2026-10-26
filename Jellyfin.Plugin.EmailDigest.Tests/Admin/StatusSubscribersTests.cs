using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.EmailDigest.Admin;
using Jellyfin.Plugin.EmailDigest.Configuration;
using Jellyfin.Plugin.EmailDigest.Delivery;
using Jellyfin.Plugin.EmailDigest.Models;
using Jellyfin.Plugin.EmailDigest.Services;
using Jellyfin.Plugin.EmailDigest.Tests.Delivery;
using Xunit;

namespace Jellyfin.Plugin.EmailDigest.Tests.Admin;

public sealed class StatusSubscribersTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "EmailDigestStatusSub_" + Guid.NewGuid());
    private readonly PluginConfiguration _config = new() { DigestEnabled = true };
    private readonly UserPreferenceRepository _preferences;
    private readonly StatusAdminService _service;

    public StatusSubscribersTests()
    {
        Directory.CreateDirectory(_folder);
        _preferences = new UserPreferenceRepository(_folder);
        _service = new StatusAdminService(new RunnerHarness.FixedConfig(_config), new RunSummaryStore(_folder), _preferences);
    }

    public void Dispose()
    {
        _preferences.Dispose();
        Directory.Delete(_folder, recursive: true);
    }

    private async Task AddAsync(string address, bool verified, bool enabled)
    {
        var id = Guid.NewGuid();
        await _preferences.UpdateAsync(
            id,
            _ => new UserNotificationPreferences { UserId = id, Email = address, EmailVerifiedUtc = verified ? DateTimeOffset.UtcNow : null, Language = "en", NotificationsEnabled = enabled },
            CancellationToken.None);
    }

    private async Task<DigestStatusDto> StatusAsync()
    {
        return (DigestStatusDto)(await _service.GetStatusAsync(CancellationToken.None)).Body!;
    }

    [Fact]
    public async Task Counts_SeparateWantingFromWillReceiveFromWaiting()
    {
        await AddAsync("a@example.com", verified: true, enabled: true);
        await AddAsync("b@example.com", verified: true, enabled: false);
        await AddAsync("c@example.com", verified: false, enabled: true);
        await AddAsync("d@example.com", verified: false, enabled: true);

        var counts = (await StatusAsync()).Subscribers!;

        Assert.Equal(new SubscriberCountsDto(4, 3, 1, 2), counts);
    }

    [Fact]
    public async Task Status_NeverContainsAnAddress()
    {
        await AddAsync("secret.person@example.com", verified: true, enabled: true);

        var json = JsonSerializer.Serialize(await StatusAsync());

        Assert.DoesNotContain("secret.person", json, StringComparison.Ordinal);
        Assert.DoesNotContain("@", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoPreferencesYet_IsZero_NotAProblem()
    {
        var status = await StatusAsync();

        Assert.Equal(new SubscriberCountsDto(0, 0, 0, 0), status.Subscribers);
        Assert.False(status.PreferencesUnreadable);
    }

    [Fact]
    public async Task UnreadablePreferences_AreReportedAsAProblem_NotAsZeroSubscribers()
    {
        await File.WriteAllTextAsync(Path.Combine(_folder, "user-preferences.json"), "{ broken");

        var status = await StatusAsync();

        Assert.Null(status.Subscribers);
        Assert.True(status.PreferencesUnreadable);
    }

    [Theory]
    [InlineData("", false, false)]
    [InlineData("https://jellyfin.example.org", true, false)]
    [InlineData("http://192.168.1.5:8096", true, true)]
    [InlineData("not a url", false, false)]
    public async Task PublicAddress_IsReportedAsSetOrNot_AndWarnedWhenPlainHttp(string value, bool set, bool insecure)
    {
        _config.PublicBaseUrl = value;

        var status = await StatusAsync();

        Assert.Equal(set, status.PublicAddressSet);
        Assert.Equal(insecure, status.PublicAddressInsecure);
        Assert.DoesNotContain(value.Length > 0 ? value : "\u0001", JsonSerializer.Serialize(status), StringComparison.Ordinal);
    }

    [Fact]
    public void AdminPage_LoadsAndSavesTheTwoNewSettings_AndShowsTheWarningsAsText()
    {
        var page = File.ReadAllText(FindPage());

        Assert.Contains("config.PublicBaseUrl = $('#PublicBaseUrl').value.trim();", page, StringComparison.Ordinal);
        Assert.Contains("config.VerificationLinkMinutes = parseInt($('#VerificationLinkMinutes').value, 10) || 0;", page, StringComparison.Ordinal);
        Assert.Contains("$('#PublicBaseUrl').value = config.PublicBaseUrl || '';", page, StringComparison.Ordinal);
        Assert.Contains("say('#PublicUrlWarning'", page, StringComparison.Ordinal);
        Assert.Contains("say('#StatusSubscribers'", page, StringComparison.Ordinal);
    }

    private static string FindPage()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Jellyfin.Plugin.EmailDigest", "Configuration", "configPage.html")))
        {
            directory = directory.Parent;
        }

        return Path.Combine(directory!.FullName, "Jellyfin.Plugin.EmailDigest", "Configuration", "configPage.html");
    }
}
