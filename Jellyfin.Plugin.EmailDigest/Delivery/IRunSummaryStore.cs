using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.EmailDigest.Delivery;

/// <summary>
/// Keeps the summary of the most recent digest run so an administrator can see it.
/// </summary>
public interface IRunSummaryStore
{
    /// <summary>
    /// Gets the last summary.
    /// </summary>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>The summary, or null when no run has been recorded or the file is unreadable.</returns>
    Task<DigestRunSummary?> GetAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Replaces the last summary.
    /// </summary>
    /// <param name="summary">The summary.</param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A task representing the save.</returns>
    Task SaveAsync(DigestRunSummary summary, CancellationToken cancellationToken);
}
