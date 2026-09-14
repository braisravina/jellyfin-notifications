using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.EmailDigest.Configuration;
using Jellyfin.Plugin.EmailDigest.Localization;
using Jellyfin.Plugin.EmailDigest.Models;

namespace Jellyfin.Plugin.EmailDigest.Services;

/// <inheritdoc />
/// <remarks>
/// Backed by a single JSON file (a plain array, not an object keyed by user id, so it stays easy
/// to hand-inspect) under the plugin's data folder. Reads and writes are serialized through an
/// in-process lock — sufficient because Jellyfin plugins run in a single server process — and
/// writes are atomic via <see cref="AtomicJsonFile"/>.
/// </remarks>
public sealed class UserPreferenceRepository : IUserPreferenceRepository, IDisposable
{
    private const string FileName = "user-preferences.json";

    private readonly string _filePath;
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="UserPreferenceRepository"/> class.
    /// </summary>
    /// <param name="dataFolderPath">The plugin's data folder path.</param>
    public UserPreferenceRepository(string dataFolderPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataFolderPath);
        _filePath = Path.Combine(dataFolderPath, FileName);
    }

    /// <inheritdoc />
    public async Task<UserNotificationPreferences?> GetAsync(Guid userId, CancellationToken cancellationToken)
    {
        var all = await ReadAllAsync(cancellationToken).ConfigureAwait(false);
        return all.FirstOrDefault(p => p.UserId == userId);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserNotificationPreferences>> GetAllAsync(CancellationToken cancellationToken)
    {
        return await ReadAllAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SaveAsync(UserNotificationPreferences preferences, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        if (!ConfigurationValidation.IsValidEmail(preferences.Email))
        {
            throw new ArgumentException($"'{preferences.Email}' is not a valid email address.", nameof(preferences));
        }

        if (!SupportedLanguages.IsSupported(preferences.Language))
        {
            throw new ArgumentException($"'{preferences.Language}' is not a supported language code.", nameof(preferences));
        }

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var all = await AtomicJsonFile.ReadAsync<List<UserNotificationPreferences>>(_filePath, cancellationToken).ConfigureAwait(false)
                      ?? new List<UserNotificationPreferences>();

            all.RemoveAll(p => p.UserId == preferences.UserId);
            all.Add(preferences);

            await AtomicJsonFile.WriteAsync(_filePath, all, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<List<UserNotificationPreferences>> ReadAllAsync(CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await AtomicJsonFile.ReadAsync<List<UserNotificationPreferences>>(_filePath, cancellationToken).ConfigureAwait(false)
                   ?? new List<UserNotificationPreferences>();
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
}
