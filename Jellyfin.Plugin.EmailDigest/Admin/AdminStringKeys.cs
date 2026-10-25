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
        "TestEmailBody",
        "AdminTitle",
        "AdminIntro",
        "AdminSectionDigest",
        "AdminDigestEnabled",
        "AdminDigestEnabledDesc",
        "AdminDefaultLanguage",
        "AdminDefaultLanguageDesc",
        "AdminLookback",
        "AdminLookbackDesc",
        "AdminSectionSmtp",
        "AdminSmtpEnabled",
        "AdminSmtpEnabledDesc",
        "AdminHost",
        "AdminPort",
        "AdminSecurity",
        "AdminSecurityStartTls",
        "AdminSecurityImplicit",
        "AdminSecurityNone",
        "AdminUnencryptedWarning",
        "AdminUsername",
        "AdminUsernameDesc",
        "AdminSenderAddress",
        "AdminSenderName",
        "AdminTimeout",
        "AdminSave",
        "AdminSaved",
        "AdminSaveFailed",
        "AdminSectionPassword",
        "AdminPasswordNew",
        "AdminPasswordDesc",
        "AdminPasswordSet",
        "AdminPasswordClear",
        "AdminPasswordStatusNone",
        "AdminPasswordStatusStored",
        "AdminPasswordStatusExternal",
        "AdminPasswordSaved",
        "AdminPasswordCleared",
        "AdminPasswordInvalid",
        "AdminPasswordExternal",
        "AdminSectionTest",
        "AdminTestSaveFirst",
        "AdminTestConnection",
        "AdminTestConnectionDesc",
        "AdminTestRecipient",
        "AdminTestSend",
        "AdminTestSendDesc",
        "AdminInvalidRecipient",
        "AdminThrottled",
        "AdminWorking",
        "AdminUnexpected"
    };
}
