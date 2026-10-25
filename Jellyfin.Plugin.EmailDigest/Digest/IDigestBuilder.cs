using System.Collections.Generic;
using Jellyfin.Plugin.EmailDigest.Models;

namespace Jellyfin.Plugin.EmailDigest.Digest;

/// <summary>
/// Turns discovered media into a digest model.
/// </summary>
/// <remarks>
/// Pure and deterministic: the same items in any order give the same model. It does not mutate its
/// input, read or write checkpoints, or touch Jellyfin.
/// </remarks>
public interface IDigestBuilder
{
    /// <summary>
    /// Builds a digest model.
    /// </summary>
    /// <param name="items">The authorized, discovered items for one recipient.</param>
    /// <param name="options">The display limits.</param>
    /// <returns>The digest model.</returns>
    DigestModel Build(IReadOnlyCollection<DiscoveredMediaItem> items, DigestOptions options);
}
