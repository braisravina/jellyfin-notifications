namespace Jellyfin.Plugin.EmailDigest.Verification;

/// <summary>
/// The result of presenting a token.
/// </summary>
public enum ConfirmOutcome
{
    /// <summary>The token was valid and the address was applied.</summary>
    Confirmed,

    /// <summary>
    /// The token is unknown, already used, expired, replaced or cancelled. These are deliberately not
    /// told apart to the caller, so a token cannot be probed.
    /// </summary>
    Invalid,

    /// <summary>The token was valid and is now used up, but recording the address failed; a new link must be requested.</summary>
    NotApplied
}
