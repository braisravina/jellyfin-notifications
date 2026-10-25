namespace Jellyfin.Plugin.EmailDigest.Delivery;

/// <summary>
/// What a run is allowed to do.
/// </summary>
public enum DigestRunMode
{
    /// <summary>
    /// The digest is disabled; nothing happens.
    /// </summary>
    Disabled = 0,

    /// <summary>
    /// Builds and renders digests and counts them; sends nothing and records nothing.
    /// </summary>
    DryRun = 1,

    /// <summary>
    /// Sends digests and records confirmed deliveries.
    /// </summary>
    Live = 2
}
