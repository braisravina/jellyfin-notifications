using System;

namespace Jellyfin.Plugin.EmailDigest.Verification;

/// <summary>
/// A request to verify one address for one user. The token itself is never stored, only its hash.
/// </summary>
public sealed class VerificationRequest
{
    /// <summary>
    /// Gets or sets the Jellyfin user the request belongs to.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Gets or sets the address being verified. It is not used for sending digests until confirmed.
    /// </summary>
    public string Address { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the SHA-256 hash of the token, as upper-case hex.
    /// </summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets when the request was made.
    /// </summary>
    public DateTimeOffset CreatedUtc { get; set; }

    /// <summary>
    /// Gets or sets when the link stops working.
    /// </summary>
    public DateTimeOffset ExpiresUtc { get; set; }
}
