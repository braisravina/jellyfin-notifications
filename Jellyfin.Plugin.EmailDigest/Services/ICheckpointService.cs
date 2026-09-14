using System;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.EmailDigest.Services;

/// <summary>
/// Persists the digest checkpoint: the start boundary of the last successfully completed digest
/// run.
/// </summary>
/// <remarks>
/// This service only stores and retrieves the checkpoint value. It has no opinion on fallback
/// lookback behavior when no checkpoint exists, and no opinion on run_start/cutoff semantics —
/// those belong to the digest orchestrator (Phase 6), which decides what to do with a missing
/// checkpoint and when to call <see cref="SaveSuccessfulRunAsync"/>.
/// </remarks>
public interface ICheckpointService
{
    /// <summary>
    /// Gets the checkpoint from the last successfully completed digest run.
    /// </summary>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The checkpoint, or <see langword="null"/> if no digest run has ever succeeded.</returns>
    Task<DateTimeOffset?> GetLastSuccessfulRunAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Records a new checkpoint after a digest run completes successfully.
    /// </summary>
    /// <param name="checkpoint">
    /// The start boundary of the run that just completed successfully. Callers should pass the
    /// time the run started, not the time it finished, so media added while the run was in
    /// progress is picked up by the next run rather than skipped.
    /// </param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A task representing the asynchronous save operation.</returns>
    Task SaveSuccessfulRunAsync(DateTimeOffset checkpoint, CancellationToken cancellationToken);
}
