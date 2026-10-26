using System.Collections.Generic;

namespace Jellyfin.Plugin.EmailDigest.Subscription;

/// <summary>
/// The localization keys of the address-verification email.
/// </summary>
public static class SubscriptionStringKeys
{
    /// <summary>
    /// Gets every key the verification email uses; each must exist, non-empty, in every language.
    /// </summary>
    public static IReadOnlyList<string> VerificationEmail { get; } = new[]
    {
        "VerifySubject",
        "VerifyIntro",
        "VerifyAction",
        "VerifyButton",
        "VerifyExpires",
        "VerifyIgnore",
        "VerifyFooter"
    };
}
