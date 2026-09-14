using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.EmailDigest.Services;

/// <inheritdoc />
/// <remarks>
/// Backed by a single small JSON file under the plugin's data folder, written atomically via
/// <see cref="AtomicJsonFile"/> so a crash mid-write cannot corrupt the checkpoint or silently
/// revert it to "no checkpoint" (which would cause duplicate notifications on next run).
/// </remarks>
public sealed class CheckpointService : ICheckpointService, IDisposable
{
    private const string FileName = "checkpoint.json";

    private readonly string _filePath;
    private readonly SemaphoreSlim _lock = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="CheckpointService"/> class.
    /// </summary>
    /// <param name="dataFolderPath">The plugin's data folder path.</param>
    public CheckpointService(string dataFolderPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataFolderPath);
        _filePath = Path.Combine(dataFolderPath, FileName);
    }

    /// <inheritdoc />
    public async Task<DateTimeOffset?> GetLastSuccessfulRunAsync(CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var record = await AtomicJsonFile.ReadAsync<CheckpointRecord>(_filePath, cancellationToken).ConfigureAwait(false);
            return record?.LastSuccessfulRunUtc;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public async Task SaveSuccessfulRunAsync(DateTimeOffset checkpoint, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var record = new CheckpointRecord { LastSuccessfulRunUtc = checkpoint };
            await AtomicJsonFile.WriteAsync(_filePath, record, cancellationToken).ConfigureAwait(false);
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

    private sealed class CheckpointRecord
    {
        public DateTimeOffset LastSuccessfulRunUtc { get; set; }
    }
}
