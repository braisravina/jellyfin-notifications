using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.EmailDigest.Configuration;
using Jellyfin.Plugin.EmailDigest.Models;

namespace Jellyfin.Plugin.EmailDigest.Services;

/// <summary>
/// Discovers newly-added supported media a specific Jellyfin user is authorized to see.
/// </summary>
/// <remarks>
/// Authorization is enforced entirely by Jellyfin itself (see
/// <see cref="MediaDiscoveryService"/>) — this service never maintains its own permission logic.
/// <c>monitoredLibraries</c> is supplied by the caller rather than read from
/// <see cref="Plugin"/> directly, so this service has no static/global dependency and stays
/// independently testable. It has no authority over the digest checkpoint: the caller decides
/// <c>fromUtc</c>/<c>toUtc</c>, and this service never advances or reads checkpoint state.
/// </remarks>
public interface IMediaDiscoveryService
{
    /// <summary>
    /// Discovers authorized digest candidates for one user within an explicit UTC window.
    /// </summary>
    /// <param name="userId">The stable Jellyfin user id to discover media for.</param>
    /// <param name="monitoredLibraries">
    /// The libraries the administrator wants considered. A library being monitored does not
    /// grant access — an item is only returned when the user is also authorized to see it.
    /// </param>
    /// <param name="fromUtc">
    /// The exclusive lower boundary: items with <c>DateCreated</c> equal to this instant are
    /// excluded.
    /// </param>
    /// <param name="toUtc">
    /// The inclusive upper boundary: items with <c>DateCreated</c> equal to this instant are
    /// included.
    /// </param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>
    /// The deduplicated, authorized, normalized candidates. Empty — never an exception and never
    /// an unrestricted fallback — when the user cannot be resolved, no libraries are monitored,
    /// the window is empty, or nothing authorized/supported/new exists.
    /// </returns>
    Task<IReadOnlyList<DiscoveredMediaItem>> DiscoverAsync(
        Guid userId,
        IReadOnlyList<MonitoredLibrary> monitoredLibraries,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        CancellationToken cancellationToken);
}
