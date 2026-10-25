using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.EmailDigest.Services;

namespace Jellyfin.Plugin.EmailDigest.Delivery;

/// <inheritdoc />
/// <remarks>
/// One JSON file, <c>delivery-state.json</c>, holds a record per user and a format version. Writes go
/// through <see cref="AtomicJsonFile"/> (temp file then rename), and the previous version is copied
/// to <c>delivery-state.json.bak</c> first, so there is always a recovery point. A file that cannot
/// be parsed, or that declares a newer format, raises <see cref="DeliveryStateException"/> and is
/// never overwritten or treated as empty. The folder is the plugin's data folder, which in Jellyfin
/// 10.11.8 is a sibling of the versioned install folder and so survives plugin upgrades.
/// </remarks>
public sealed class DeliveryStateStore : IDeliveryStateStore, IDisposable
{
    private const string FileName = "delivery-state.json";
    private const string LegacyFileName = "checkpoint.json";
    private const int CurrentFormat = 1;

    private readonly string _path;
    private readonly string _legacyPath;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="DeliveryStateStore"/> class.
    /// </summary>
    /// <param name="dataFolderPath">The plugin's data folder.</param>
    /// <param name="time">The clock used to prune old delivered items.</param>
    public DeliveryStateStore(string dataFolderPath, TimeProvider time)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataFolderPath);
        ArgumentNullException.ThrowIfNull(time);
        _path = Path.Combine(dataFolderPath, FileName);
        _legacyPath = Path.Combine(dataFolderPath, LegacyFileName);
        _time = time;
    }

    /// <inheritdoc />
    public async Task<UserDeliveryState?> GetAsync(Guid userId, CancellationToken cancellationToken)
    {
        var all = await GetAllAsync(cancellationToken).ConfigureAwait(false);
        return all.FirstOrDefault(s => s.UserId == userId);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<UserDeliveryState>> GetAllAsync(CancellationToken cancellationToken)
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

    /// <inheritdoc />
    public async Task SaveAsync(UserDeliveryState state, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.UserId == Guid.Empty)
        {
            throw new ArgumentException("A delivery state needs a user id.", nameof(state));
        }

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var all = (await ReadAsync(cancellationToken).ConfigureAwait(false)).ToList();

            var now = _time.GetUtcNow();
            state.UpdatedUtc = now;
            state.PruneDelivered(now);

            all.RemoveAll(s => s.UserId == state.UserId);
            all.Add(state);

            KeepBackup();
            var file = new StateFile { Format = CurrentFormat, Users = all.OrderBy(s => s.UserId).ToList() };
            await AtomicJsonFile.WriteAsync(_path, file, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<DateTimeOffset?> GetLegacyCheckpointAsync(CancellationToken cancellationToken)
    {
        try
        {
            var legacy = await AtomicJsonFile.ReadAsync<LegacyCheckpoint>(_legacyPath, cancellationToken).ConfigureAwait(false);
            return legacy is null || legacy.LastSuccessfulRunUtc == default ? null : legacy.LastSuccessfulRunUtc;
        }
        catch (JsonException)
        {
            // A damaged legacy hint is ignored: the bounded lookback baseline applies instead.
            return null;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _lock.Dispose();
    }

    private async Task<IReadOnlyList<UserDeliveryState>> ReadAsync(CancellationToken cancellationToken)
    {
        StateFile? file;
        try
        {
            file = await AtomicJsonFile.ReadAsync<StateFile>(_path, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException ex)
        {
            throw new DeliveryStateException("The delivery state file is damaged and was left untouched.", ex);
        }

        if (file is null)
        {
            return Array.Empty<UserDeliveryState>();
        }

        if (file.Format != CurrentFormat || file.Users is null)
        {
            throw new DeliveryStateException("The delivery state file has an unsupported format and was left untouched.");
        }

        if (file.Users.Any(u => u is null || u.UserId == Guid.Empty))
        {
            throw new DeliveryStateException("The delivery state file contains an invalid record and was left untouched.");
        }

        return file.Users;
    }

    private void KeepBackup()
    {
        try
        {
            if (File.Exists(_path))
            {
                File.Copy(_path, _path + ".bak", overwrite: true);
            }
        }
        catch (IOException)
        {
            // The backup is a convenience; failing to make it must not block recording a delivery.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed class StateFile
    {
        public int Format { get; set; }

        public List<UserDeliveryState>? Users { get; set; }
    }

    private sealed class LegacyCheckpoint
    {
        public DateTimeOffset LastSuccessfulRunUtc { get; set; }
    }
}
