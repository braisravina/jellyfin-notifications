using System;

namespace Jellyfin.Plugin.EmailDigest.Verification;

/// <summary>
/// What a user may see of their own outstanding request: never the token or its hash.
/// </summary>
/// <param name="Address">The address awaiting confirmation.</param>
/// <param name="RequestedUtc">When it was requested.</param>
/// <param name="ExpiresUtc">When the link stops working.</param>
public sealed record PendingVerification(string Address, DateTimeOffset RequestedUtc, DateTimeOffset ExpiresUtc);
