using System;
using System.IO;
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

    public void Dispose()
    {
        _repository.Dispose();
        Directory.Delete(_dataFolder, recursive: true);
    }
}
