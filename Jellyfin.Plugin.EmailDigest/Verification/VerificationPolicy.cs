using System;

namespace Jellyfin.Plugin.EmailDigest.Verification;

/// <summary>
/// How long a verification link lives and how often addresses may be submitted.
/// </summary>
/// <param name="Lifetime">How long a link stays valid.</param>
/// <param name="Cooldown">The least time between two requests by one user.</param>
/// <param name="PerUserPerHour">Most requests by one user in an hour.</param>
/// <param name="PerUserPerDay">Most requests by one user in 24 hours.</param>
/// <param name="PerAddressPerDay">Most requests, from any user, naming one address in 24 hours.</param>
/// <param name="GlobalPerHour">Most requests overall in an hour, which bounds what this plugin can ask the SMTP server to send.</param>
public sealed record VerificationPolicy(
    TimeSpan Lifetime,
    TimeSpan Cooldown,
    int PerUserPerHour,
    int PerUserPerDay,
    int PerAddressPerDay,
    int GlobalPerHour)
{
    /// <summary>
    /// The shortest accepted link lifetime, in minutes.
    /// </summary>
    public const int MinLifetimeMinutes = 5;

    /// <summary>
    /// The longest accepted link lifetime, in minutes (24 hours).
    /// </summary>
    public const int MaxLifetimeMinutes = 1440;

    /// <summary>
    /// The default link lifetime, in minutes.
    /// </summary>
    public const int DefaultLifetimeMinutes = 60;

    /// <summary>
    /// Gets the default policy: a one-hour link, one request a minute, three an hour and ten a day per
    /// user, three a day per address, and sixty an hour overall.
    /// </summary>
    public static VerificationPolicy Default { get; } = ForLifetime(DefaultLifetimeMinutes);

    /// <summary>
    /// Gets the default policy with a chosen link lifetime.
    /// </summary>
    /// <param name="lifetimeMinutes">The lifetime in minutes, clamped to the accepted range.</param>
    /// <returns>The policy.</returns>
    public static VerificationPolicy ForLifetime(int lifetimeMinutes)
    {
        var minutes = Math.Clamp(lifetimeMinutes, MinLifetimeMinutes, MaxLifetimeMinutes);
        return new VerificationPolicy(TimeSpan.FromMinutes(minutes), TimeSpan.FromMinutes(1), 3, 10, 3, 60);
    }
}
