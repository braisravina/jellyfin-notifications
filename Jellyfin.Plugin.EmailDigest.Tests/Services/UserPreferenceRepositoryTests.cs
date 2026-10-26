using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.EmailDigest.Models;
using Jellyfin.Plugin.EmailDigest.Services;
using Xunit;

namespace Jellyfin.Plugin.EmailDigest.Tests.Services;

public sealed class UserPreferenceRepositoryTests : IDisposable
{
    private readonly string _dataFolder;
    private readonly UserPreferenceRepository _repository;

    public UserPreferenceRepositoryTests()
    {
        _dataFolder = Path.Combine(Path.GetTempPath(), "EmailDigestTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_dataFolder);
        _repository = new UserPreferenceRepository(_dataFolder);
    }

    [Fact]
    public async Task GetAsync_UnknownUser_ReturnsNull()
    {
        var result = await _repository.GetAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task SaveAsync_ThenGetAsync_RoundTrips()
    {
        var userId = Guid.NewGuid();
        var preferences = new UserNotificationPreferences
        {
            UserId = userId,
            Email = "brais@example.com",
            Language = "gl",
            NotificationsEnabled = true
        };

        await _repository.SaveAsync(preferences, CancellationToken.None);
        var result = await _repository.GetAsync(userId, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(userId, result!.UserId);
        Assert.Equal("brais@example.com", result.Email);
        Assert.Equal("gl", result.Language);
        Assert.True(result.NotificationsEnabled);
    }

    [Fact]
    public async Task SaveAsync_TwoDifferentUsers_DoesNotClobberEachOther()
    {
        var userA = new UserNotificationPreferences { UserId = Guid.NewGuid(), Email = "a@example.com", Language = "en", NotificationsEnabled = true };
        var userB = new UserNotificationPreferences { UserId = Guid.NewGuid(), Email = "b@example.com", Language = "es", NotificationsEnabled = false };

        await _repository.SaveAsync(userA, CancellationToken.None);
        await _repository.SaveAsync(userB, CancellationToken.None);

        var resultA = await _repository.GetAsync(userA.UserId, CancellationToken.None);
        var resultB = await _repository.GetAsync(userB.UserId, CancellationToken.None);

        Assert.Equal("a@example.com", resultA!.Email);
        Assert.Equal("b@example.com", resultB!.Email);
    }

    [Fact]
    public async Task SaveAsync_SameUserTwice_ReplacesPreviousRecord()
    {
        var userId = Guid.NewGuid();
        await _repository.SaveAsync(new UserNotificationPreferences { UserId = userId, Email = "old@example.com", Language = "en", NotificationsEnabled = true }, CancellationToken.None);
        await _repository.SaveAsync(new UserNotificationPreferences { UserId = userId, Email = "new@example.com", Language = "en", NotificationsEnabled = false }, CancellationToken.None);

        var all = await _repository.GetAllAsync(CancellationToken.None);
        var result = await _repository.GetAsync(userId, CancellationToken.None);

        Assert.Single(all);
        Assert.Equal("new@example.com", result!.Email);
        Assert.False(result.NotificationsEnabled);
    }

    [Fact]
    public async Task GetAllAsync_ReturnsEverySavedUser()
    {
        await _repository.SaveAsync(new UserNotificationPreferences { UserId = Guid.NewGuid(), Email = "a@example.com", Language = "en", NotificationsEnabled = true }, CancellationToken.None);
        await _repository.SaveAsync(new UserNotificationPreferences { UserId = Guid.NewGuid(), Email = "b@example.com", Language = "en", NotificationsEnabled = true }, CancellationToken.None);
        await _repository.SaveAsync(new UserNotificationPreferences { UserId = Guid.NewGuid(), Email = "c@example.com", Language = "en", NotificationsEnabled = true }, CancellationToken.None);

        var all = await _repository.GetAllAsync(CancellationToken.None);

        Assert.Equal(3, all.Count);
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("")]
    public async Task SaveAsync_InvalidEmail_ThrowsWithoutPersisting(string invalidEmail)
    {
        var preferences = new UserNotificationPreferences { UserId = Guid.NewGuid(), Email = invalidEmail, Language = "en", NotificationsEnabled = true };

        await Assert.ThrowsAsync<ArgumentException>(() => _repository.SaveAsync(preferences, CancellationToken.None));

        var all = await _repository.GetAllAsync(CancellationToken.None);
        Assert.Empty(all);
    }

    [Fact]
    public async Task SaveAsync_UnsupportedLanguage_ThrowsWithoutPersisting()
    {
        var preferences = new UserNotificationPreferences { UserId = Guid.NewGuid(), Email = "a@example.com", Language = "fr", NotificationsEnabled = true };

        await Assert.ThrowsAsync<ArgumentException>(() => _repository.SaveAsync(preferences, CancellationToken.None));

        var all = await _repository.GetAllAsync(CancellationToken.None);
        Assert.Empty(all);
    }

    [Fact]
    public async Task SaveAsync_LeavesNoTempFileBehind()
    {
        await _repository.SaveAsync(new UserNotificationPreferences { UserId = Guid.NewGuid(), Email = "a@example.com", Language = "en", NotificationsEnabled = true }, CancellationToken.None);

        Assert.DoesNotContain(Directory.GetFiles(_dataFolder), f => f.EndsWith(".tmp", StringComparison.Ordinal));
        Assert.Contains(Directory.GetFiles(_dataFolder), f => f.EndsWith("user-preferences.json", StringComparison.Ordinal));
    }

    private string PreferencesPath => Path.Combine(_dataFolder, "user-preferences.json");

    [Fact]
    public async Task LegacyBareArray_IsReadAsUnverified_AndConvertedOnFirstWrite_KeepingTheOriginalOnce()
    {
        var user = Guid.NewGuid();
        var legacy = "[{\"UserId\":\"" + user + "\",\"Email\":\"ana@example.com\",\"Language\":\"es\",\"NotificationsEnabled\":true}]";
        await File.WriteAllTextAsync(PreferencesPath, legacy);

        var read = await _repository.GetAsync(user, CancellationToken.None);
        Assert.NotNull(read);
        Assert.Null(read!.EmailVerifiedUtc);
        Assert.False(read.CanReceiveDigest());
        Assert.Equal(legacy, await File.ReadAllTextAsync(PreferencesPath));

        await _repository.UpdateAsync(user, p => { p!.Language = "gl"; return p; }, CancellationToken.None);
        await _repository.UpdateAsync(user, p => { p!.Language = "en"; return p; }, CancellationToken.None);

        Assert.Equal(legacy, await File.ReadAllTextAsync(PreferencesPath + ".v1.bak"));
        Assert.Contains("\"Format\": 2", await File.ReadAllTextAsync(PreferencesPath), StringComparison.Ordinal);
        Assert.Equal("en", (await _repository.GetAsync(user, CancellationToken.None))!.Language);
    }

    [Fact]
    public void CanReceiveDigest_RequiresEnabledAndVerifiedAndValidAddress()
    {
        var verified = new UserNotificationPreferences { Email = "a@example.com", EmailVerifiedUtc = DateTimeOffset.UtcNow, NotificationsEnabled = true };
        Assert.True(verified.CanReceiveDigest());

        verified.NotificationsEnabled = false;
        Assert.False(verified.CanReceiveDigest());
        verified.NotificationsEnabled = true;
        verified.EmailVerifiedUtc = null;
        Assert.False(verified.CanReceiveDigest());
        verified.EmailVerifiedUtc = DateTimeOffset.UtcNow;
        verified.Email = "not-an-address";
        Assert.False(verified.CanReceiveDigest());
        verified.Email = "a@example.com\nBcc: x@example.com";
        Assert.False(verified.CanReceiveDigest());
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("42")]
    [InlineData("{\"Format\":3,\"Users\":[]}")]
    [InlineData("{\"Format\":2}")]
    [InlineData("[{\"UserId\":\"00000000-0000-0000-0000-000000000000\"}]")]
    public async Task DamagedOrUnsupportedFile_ThrowsAndIsNeverOverwritten(string content)
    {
        await File.WriteAllTextAsync(PreferencesPath, content);

        await Assert.ThrowsAsync<UserPreferencesException>(() => _repository.GetAllAsync(CancellationToken.None));
        await Assert.ThrowsAsync<UserPreferencesException>(() => _repository.UpdateAsync(Guid.NewGuid(), _ => new UserNotificationPreferences { UserId = default }, CancellationToken.None));
        await Assert.ThrowsAsync<UserPreferencesException>(() => _repository.SaveAsync(new UserNotificationPreferences { UserId = Guid.NewGuid(), Email = "a@example.com" }, CancellationToken.None));

        Assert.Equal(content, await File.ReadAllTextAsync(PreferencesPath));
        Assert.False(File.Exists(PreferencesPath + ".v1.bak"));
    }

    [Fact]
    public async Task RepeatedUserId_IsRejectedAsInvalid()
    {
        var id = Guid.NewGuid();
        await File.WriteAllTextAsync(PreferencesPath, "[{\"UserId\":\"" + id + "\"},{\"UserId\":\"" + id + "\"}]");

        await Assert.ThrowsAsync<UserPreferencesException>(() => _repository.GetAllAsync(CancellationToken.None));
    }

    [Fact]
    public async Task UpdateAsync_CreatesARecordWithoutAnAddress_ForALanguageOnlyChoice()
    {
        var user = Guid.NewGuid();

        var saved = await _repository.UpdateAsync(user, p => new UserNotificationPreferences { UserId = user, Language = "gl" }, CancellationToken.None);

        Assert.Equal(string.Empty, saved!.Email);
        Assert.False(saved.CanReceiveDigest());
    }

    [Fact]
    public async Task UpdateAsync_CannotWriteAnotherUsersRecord()
    {
        var mine = Guid.NewGuid();
        var other = Guid.NewGuid();
        await _repository.SaveAsync(new UserNotificationPreferences { UserId = other, Email = "o@example.com", Language = "en", NotificationsEnabled = true }, CancellationToken.None);

        await Assert.ThrowsAsync<ArgumentException>(() => _repository.UpdateAsync(mine, _ => new UserNotificationPreferences { UserId = other, Email = "x@example.com" }, CancellationToken.None));

        Assert.Equal("o@example.com", (await _repository.GetAsync(other, CancellationToken.None))!.Email);
    }

    [Fact]
    public async Task UpdateAsync_RejectsAVerifiedStampWithoutAnAddress_AndALineBreakInTheAddress()
    {
        var user = Guid.NewGuid();

        await Assert.ThrowsAsync<ArgumentException>(() => _repository.UpdateAsync(user, _ => new UserNotificationPreferences { UserId = user, EmailVerifiedUtc = DateTimeOffset.UtcNow }, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => _repository.UpdateAsync(user, _ => new UserNotificationPreferences { UserId = user, Email = "a@example.com\r\nBcc: v@example.com" }, CancellationToken.None));
        Assert.Empty(await _repository.GetAllAsync(CancellationToken.None));
    }

    [Fact]
    public async Task UpdateAsync_ReturningNull_RemovesOnlyThatRecord()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        await _repository.SaveAsync(new UserNotificationPreferences { UserId = a, Email = "a@example.com", Language = "en" }, CancellationToken.None);
        await _repository.SaveAsync(new UserNotificationPreferences { UserId = b, Email = "b@example.com", Language = "en" }, CancellationToken.None);

        await _repository.UpdateAsync(a, _ => null, CancellationToken.None);

        Assert.Null(await _repository.GetAsync(a, CancellationToken.None));
        Assert.NotNull(await _repository.GetAsync(b, CancellationToken.None));
    }

    [Fact]
    public async Task ConcurrentUpdates_OfDifferentFields_NeitherIsLost()
    {
        var user = Guid.NewGuid();
        await _repository.SaveAsync(new UserNotificationPreferences { UserId = user, Email = "a@example.com", Language = "en", NotificationsEnabled = false }, CancellationToken.None);

        await Task.WhenAll(
            Enumerable.Range(0, 20).Select(i => i % 2 == 0
                ? _repository.UpdateAsync(user, p => { p!.Language = "gl"; return p; }, CancellationToken.None)
                : _repository.UpdateAsync(user, p => { p!.NotificationsEnabled = true; return p; }, CancellationToken.None)));

        var result = await _repository.GetAsync(user, CancellationToken.None);
        Assert.Equal("gl", result!.Language);
        Assert.True(result.NotificationsEnabled);
    }

    [Fact]
    public async Task ConcurrentUpdates_OfDifferentUsers_AreAllKept()
    {
        var ids = Enumerable.Range(0, 25).Select(_ => Guid.NewGuid()).ToList();

        await Task.WhenAll(ids.Select(id => _repository.UpdateAsync(id, _ => new UserNotificationPreferences { UserId = id, Language = "es" }, CancellationToken.None)));

        Assert.Equal(25, (await _repository.GetAllAsync(CancellationToken.None)).Count);
    }

    [Fact]
    public async Task ChangeFunctionException_LeavesTheFileUnchanged()
    {
        var user = Guid.NewGuid();
        await _repository.SaveAsync(new UserNotificationPreferences { UserId = user, Email = "a@example.com", Language = "en" }, CancellationToken.None);
        var before = await File.ReadAllTextAsync(PreferencesPath);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _repository.UpdateAsync(user, _ => throw new InvalidOperationException(), CancellationToken.None));

        Assert.Equal(before, await File.ReadAllTextAsync(PreferencesPath));
    }

    [Fact]
    public async Task VerifiedAddress_SurvivesARoundTrip()
    {
        var user = Guid.NewGuid();
        var when = new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero);
        await _repository.SaveAsync(new UserNotificationPreferences { UserId = user, Email = "a@example.com", EmailVerifiedUtc = when, Language = "en", NotificationsEnabled = true }, CancellationToken.None);

        var result = await _repository.GetAsync(user, CancellationToken.None);

        Assert.Equal(when, result!.EmailVerifiedUtc);
        Assert.True(result.CanReceiveDigest());
    }

    [Fact]
    public async Task NewPreferencesFile_IsOwnerOnly_OnUnix()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        await _repository.UpdateAsync(Guid.NewGuid(), _ => null, CancellationToken.None);
        var user = Guid.NewGuid();
        await _repository.UpdateAsync(user, _ => new UserNotificationPreferences { UserId = user, Language = "en" }, CancellationToken.None);

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(PreferencesPath));
    }

    public void Dispose()
    {
        _repository.Dispose();
        Directory.Delete(_dataFolder, recursive: true);
    }
}
