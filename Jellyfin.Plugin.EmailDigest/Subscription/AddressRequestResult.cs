using System;
using Jellyfin.Plugin.EmailDigest.Verification;

namespace Jellyfin.Plugin.EmailDigest.Subscription;

/// <summary>
/// The result of asking to use a new notification address.
/// </summary>
/// <param name="Status">What happened.</param>
/// <param name="ExpiresUtc">When the confirmation link stops working, when one was sent.</param>
/// <param name="RateLimit">Which limit applied, when rate limited.</param>
/// <param name="RetryAfter">How long to wait, when rate limited.</param>
public sealed record AddressRequestResult(
    AddressRequestStatus Status,
    DateTimeOffset? ExpiresUtc = null,
    RateLimitKind? RateLimit = null,
    TimeSpan? RetryAfter = null);
