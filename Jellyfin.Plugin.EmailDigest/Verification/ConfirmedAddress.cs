using System;

namespace Jellyfin.Plugin.EmailDigest.Verification;

/// <summary>
/// The address a confirmed token proved.
/// </summary>
/// <param name="UserId">The user the address is for.</param>
/// <param name="Address">The confirmed address.</param>
public sealed record ConfirmedAddress(Guid UserId, string Address);
