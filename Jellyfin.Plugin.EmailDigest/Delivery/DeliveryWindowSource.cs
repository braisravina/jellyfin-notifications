namespace Jellyfin.Plugin.EmailDigest.Delivery;

/// <summary>
/// Where the start of a delivery window came from.
/// </summary>
public enum DeliveryWindowSource
{
    /// <summary>
    /// The user's own checkpoint.
    /// </summary>
    Checkpoint = 0,

    /// <summary>
    /// The user's checkpoint was older than the catch-up limit and was moved forward to it.
    /// </summary>
    CatchUpLimit = 1,

    /// <summary>
    /// The user has no state yet; the bounded fallback lookback applies (possibly empty).
    /// </summary>
    NewUserBaseline = 2,

    /// <summary>
    /// The user has no state yet, and a checkpoint from a version before per-user state is used.
    /// </summary>
    LegacyCheckpoint = 3
}
