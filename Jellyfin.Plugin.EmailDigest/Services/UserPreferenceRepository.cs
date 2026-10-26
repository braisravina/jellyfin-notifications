using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.EmailDigest.Configuration;
using Jellyfin.Plugin.EmailDigest.Localization;
using Jellyfin.Plugin.EmailDigest.Models;

namespace Jellyfin.Plugin.EmailDigest.Services;

/// <inheritdoc />
/// <remarks>
/// <para>
/// Backed by <c>user-preferences.json</c>, format 2: an object with a <c>Format</c> number and a
/// <c>Users</c> array. Format 1 (what earlier versions and hand-seeded files use) was a bare array of
/// records; it is read as is, with every address unverified, and converted on the first write after the
/// original is kept once as <c>user-preferences.json.v1.bak</c>.
/// </para>
/// <para>
/// A damaged file, a newer format or an invalid record (empty id, repeated id) raises
/// <see cref="UserPreferencesException"/> and is never overwritten, so a bad edit cannot silently
/// become "everyone unsubscribed" or "everyone forgotten". Every read-modify-write runs under one
/// in-process lock — sufficient because a Jellyfin plugin runs in a single server process — and the
/// write is atomic via <see cref="AtomicJsonFile"/>.
/// </para>
/// </remarks>
public sealed class UserPreferenceRepository : IUserPreferenceRepository, IDisposable
{
    private const string FileName = "user-preferences.json";
    private const int CurrentFormat = 2;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _filePath;
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="UserPreferenceRepository"/> class.
    /// </summary>
    /// <param name="dataFolderPath">The folder holding plugin state.</param>
    public UserPreferenceRepository(string dataFolderPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataFolderPath);
        _filePath = Path.Combine(dataFolderPath, FileName);
    }

    /// <inheritdoc />
    public async Task<UserNotificationPreferences?> GetAsync(Guid userId, CancellationToken cancellationToken)
    {
        var all = await ReadLockedAsync(cancellationToken).ConfigureAwait(false);
        return all.Users.FirstOrDefault(p => p.UserId == userId);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserNotificationPreferences>> GetAllAsync(CancellationToken cancellationToken)
    {
        return (await ReadLockedAsync(cancellationToken).ConfigureAwait(false)).Users;
    }

    /// <inheritdoc />
    public async Task SaveAsync(UserNotificationPreferences preferences, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        if (!ConfigurationValidation.IsValidEmail(preferences.Email))
        {
            throw new ArgumentException("The email address is not valid.", nameof(preferences));
        }

        await UpdateAsync(preferences.UserId, _ => Copy(preferences), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<UserNotificationPreferences?> UpdateAsync(
        Guid userId,
        Func<UserNotificationPreferences?, UserNotificationPreferences?> change,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("A user id is required.", nameof(userId));
        }

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var loaded = await ReadAsync(cancellationToken).ConfigureAwait(false);
            var current = loaded.Users.FirstOrDefault(p => p.UserId == userId);
            var result = change(current is null ? null : Copy(current));

            if (result is not null)
            {
                Validate(userId, result);
            }

            var users = loaded.Users.Where(p => p.UserId != userId).ToList();
            if (result is not null)
            {
                users.Add(result);
            }

            if (loaded.WasLegacy)
            {
                KeepOriginalOnce();
            }

            KeepBackup();
            await AtomicJsonFile.WriteAsync(_filePath, new PreferencesFile { Format = CurrentFormat, Users = users }, cancellationToken).ConfigureAwait(false);
            return result;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _lock.Dispose();
    }

    private static UserNotificationPreferences Copy(UserNotificationPreferences source)
    {
        return new UserNotificationPreferences
        {
            UserId = source.UserId,
            Email = source.Email,
            EmailVerifiedUtc = source.EmailVerifiedUtc,
            Language = source.Language,
            NotificationsEnabled = source.NotificationsEnabled
        };
    }

    private static void Validate(Guid userId, UserNotificationPreferences result)
    {
        if (result.UserId != userId)
        {
            throw new ArgumentException("A change may only affect the user it was made for.", nameof(result));
        }

        var email = result.Email ?? string.Empty;
        if (email.Length > 0 && (!ConfigurationValidation.IsValidEmail(email) || !ConfigurationValidation.IsValidSingleLine(email, 254)))
        {
            throw new ArgumentException("The email address is not valid.", nameof(result));
        }

        if (!SupportedLanguages.IsSupported(result.Language))
        {
            throw new ArgumentException("The language is not supported.", nameof(result));
        }

        if (email.Length == 0 && result.EmailVerifiedUtc is not null)
        {
            throw new ArgumentException("There is no address to have verified.", nameof(result));
        }
    }

    private async Task<Loaded> ReadLockedAsync(CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<Loaded> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
        {
            return new Loaded(new List<UserNotificationPreferences>(), false);
        }

        try
        {
            byte[] bytes;
            using (var stream = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var buffer = new MemoryStream())
            {
                await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
                bytes = buffer.ToArray();
            }

            using var document = JsonDocument.Parse(bytes);
            List<UserNotificationPreferences>? users;
            bool legacy;
            switch (document.RootElement.ValueKind)
            {
                case JsonValueKind.Array:
                    users = document.RootElement.Deserialize<List<UserNotificationPreferences>>(JsonOptions);
                    legacy = true;
                    break;
                case JsonValueKind.Object:
                    var file = document.RootElement.Deserialize<PreferencesFile>(JsonOptions);
                    if (file is null || file.Format != CurrentFormat)
                    {
                        throw new UserPreferencesException("The preferences file has an unsupported format and was left untouched.");
                    }

                    users = file.Users;
                    legacy = false;
                    break;
                default:
                    throw new UserPreferencesException("The preferences file is damaged and was left untouched.");
            }

            if (users is null || users.Any(u => u is null || u.UserId == Guid.Empty) || users.GroupBy(u => u.UserId).Any(g => g.Count() > 1))
            {
                throw new UserPreferencesException("The preferences file contains an invalid record and was left untouched.");
            }

            return new Loaded(users, legacy);
        }
        catch (JsonException ex)
        {
            throw new UserPreferencesException("The preferences file is damaged and was left untouched.", ex);
        }
    }

    // The original format-1 file is kept once, before the first conversion. If it cannot be kept the
    // write is abandoned: converting without a recovery copy is not worth the risk.
    private void KeepOriginalOnce()
    {
        var original = _filePath + ".v1.bak";
        if (!File.Exists(original))
        {
            File.Copy(_filePath, original, overwrite: false);
        }
    }

    private void KeepBackup()
    {
        try
        {
            if (File.Exists(_filePath))
            {
                File.Copy(_filePath, _filePath + ".bak", overwrite: true);
            }
        }
        catch (IOException)
        {
            // A convenience copy; failing to make it must not block saving.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed record Loaded(List<UserNotificationPreferences> Users, bool WasLegacy);

    private sealed class PreferencesFile
    {
        public int Format { get; set; }

        public List<UserNotificationPreferences>? Users { get; set; }
    }
}
