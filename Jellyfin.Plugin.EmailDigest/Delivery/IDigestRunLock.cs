using System;

namespace Jellyfin.Plugin.EmailDigest.Delivery;

/// <summary>
/// Keeps two digest runs from overlapping.
/// </summary>
public interface IDigestRunLock
{
    /// <summary>
    /// Tries to take the lock without waiting.
    /// </summary>
    /// <returns>A handle that releases the lock when disposed, or null when a run is already in progress.</returns>
    IDisposable? TryAcquire();
}
