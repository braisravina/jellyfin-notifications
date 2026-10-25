using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.EmailDigest.Services;

namespace Jellyfin.Plugin.EmailDigest.Delivery;

/// <inheritdoc />
public sealed class RunSummaryStore : IRunSummaryStore
{
    private readonly string _path;

    /// <summary>
    /// Initializes a new instance of the <see cref="RunSummaryStore"/> class.
    /// </summary>
    /// <param name="dataFolderPath">The plugin's data folder.</param>
    public RunSummaryStore(string dataFolderPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataFolderPath);
        _path = Path.Combine(dataFolderPath, "last-run.json");
    }

    /// <inheritdoc />
    public async Task<DigestRunSummary?> GetAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await AtomicJsonFile.ReadAsync<DigestRunSummary>(_path, cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public Task SaveAsync(DigestRunSummary summary, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(summary);
        return AtomicJsonFile.WriteAsync(_path, summary, cancellationToken);
    }
}
