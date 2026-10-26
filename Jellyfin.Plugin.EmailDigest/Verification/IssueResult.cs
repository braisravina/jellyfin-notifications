using System;

namespace Jellyfin.Plugin.EmailDigest.Verification;

/// <summary>
/// Why a request for a verification link was not issued.
/// </summary>
public enum RateLimitKind
{
    /// <summary>The user asked again too soon.</summary>
    Cooldown,

    /// <summary>The user asked too often.</summary>
    User,

    /// <summary>Too many requests named this address, from anyone.</summary>
    Address,

    /// <summary>Too many requests were made overall.</summary>
    Global
}

/// <summary>
/// The result of asking for a verification link.
/// </summary>
public sealed class IssueResult
{
    private IssueResult()
    {
    }

    /// <summary>
    /// Gets a value indicating whether a link was issued.
    /// </summary>
    public bool Issued => Token is not null;

    /// <summary>
    /// Gets a value indicating whether the address was not acceptable (not a valid single-line address).
    /// </summary>
    public bool InvalidAddress { get; private init; }

    /// <summary>
    /// Gets the rate limit that applied, or null.
    /// </summary>
    public RateLimitKind? RateLimit { get; private init; }

    /// <summary>
    /// Gets how long to wait before trying again when rate limited.
    /// </summary>
    public TimeSpan RetryAfter { get; private init; }

    /// <summary>
    /// Gets the one-time token; it exists only here, to be put in the email, and is never stored.
    /// </summary>
    public string? Token { get; private init; }

    /// <summary>
    /// Gets the normalized address the token is for.
    /// </summary>
    public string? Address { get; private init; }

    /// <summary>
    /// Gets when the link stops working.
    /// </summary>
    public DateTimeOffset ExpiresUtc { get; private init; }

    internal static IssueResult Ok(string token, string address, DateTimeOffset expires) =>
        new() { Token = token, Address = address, ExpiresUtc = expires };

    internal static IssueResult Invalid() => new() { InvalidAddress = true };

    internal static IssueResult Limited(RateLimitKind kind, TimeSpan retryAfter) =>
        new() { RateLimit = kind, RetryAfter = retryAfter };
}
