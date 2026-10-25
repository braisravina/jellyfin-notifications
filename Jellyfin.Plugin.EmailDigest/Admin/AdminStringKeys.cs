using System.Collections.Generic;

namespace Jellyfin.Plugin.EmailDigest.Admin;

/// <summary>
/// The localization keys the administrator page needs. The strings endpoint returns exactly these,
/// so the page can never request an arbitrary resource key.
/// </summary>
public static class AdminStringKeys
{
    /// <summary>
    /// Gets every administrator-page key.
    /// </summary>
    public static IReadOnlyList<string> All { get; } = new[]
    {
        "AdminOutcomeAccepted",
        "AdminOutcomeInvalidRequest",
        "AdminOutcomeConnectionFailed",
        "AdminOutcomeTlsFailed",
        "AdminOutcomeAuthenticationFailed",
        "AdminOutcomeSenderRejected",
        "AdminOutcomeRecipientRejected",
        "AdminOutcomeTimeout",
        "AdminOutcomeCancelled",
        "AdminOutcomeUnexpectedError",
        "TestEmailSubject",
        "TestEmailBody"
    };
}
