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
/// <c>delivery-status.json</c>, format 1. Informational only, so a damaged or unsupported file is moved
/// aside and the store starts empty rather than blocking anything.
/// </remarks>
public sealed class DeliveryStatusStore : IDeliveryStatusStore, IDisposable
{
    private const string FileName = "delivery-status.json";
    private const int CurrentFormat = 1;

    private readonly string _path;
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="DeliveryStatusStore"/> class.
    /// </summary>
    /// <param name="folder">The folder holding plugin state.</param>
    public DeliveryStatusStore(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        _path = Path.Combine(folder, FileName);
    }

    /// <inheritdoc />
    public async Task<UserDeliveryStatus?> GetAsync(Guid userId, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return (await LoadAsync(cancellationToken).ConfigureAwait(false)).FirstOrDefault(s => s.UserId == userId);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public async Task RecordAttemptAsync(Guid userId, DeliveryAttemptStatus status, DateTimeOffset atUtc, CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("A user id is required.", nameof(userId));
        }

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var all = await LoadAsync(cancellationToken).ConfigureAwait(false);
            var current = all.FirstOrDefault(s => s.UserId == userId);
            if (current is null)
            {
                current = new UserDeliveryStatus { UserId = userId };
                all.Add(current);
            }

            current.LastAttemptUtc = atUtc;
            current.LastAttemptStatus = status;
            if (status == DeliveryAttemptStatus.Accepted)
            {
                current.LastAcceptedUtc = atUtc;
            }

            await AtomicJsonFile.WriteAsync(_path, new StatusFile { Format = CurrentFormat, Users = all }, cancellationToken).ConfigureAwait(false);
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

    private async Task<List<UserDeliveryStatus>> LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            var file = await AtomicJsonFile.ReadAsync<StatusFile>(_path, cancellationToken).ConfigureAwait(false);
            if (file is null)
            {
                return new List<UserDeliveryStatus>();
            }

            if (file.Format == CurrentFormat && file.Users is not null && file.Users.All(u => u is not null && u.UserId != Guid.Empty))
            {
                return file.Users;
            }
        }
        catch (JsonException)
        {
        }

        File.Move(_path, _path + ".damaged-" + DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture), overwrite: true);
        return new List<UserDeliveryStatus>();
    }

    private sealed class StatusFile
    {
        public int Format { get; set; }

        public List<UserDeliveryStatus>? Users { get; set; }
    }
}
