using System.Collections.Generic;

namespace Jellyfin.Plugin.EmailDigest.UserApi;

/// <summary>
/// The localization keys the user and link pages need. The strings endpoint returns exactly these, so a
/// page can never ask for another resource key.
/// </summary>
public static class UserStringKeys
{
    /// <summary>
    /// Gets every user-page key.
    /// </summary>
    public static IReadOnlyList<string> All { get; } = new[]
    {
        "UserTitle",
        "UserIntro",
        "UserBackToJellyfin",
        "UserSignInRequired",
        "UserLoading",
        "UserLoadError",
        "UserForbidden",
        "UserSectionNotifications",
        "UserNotificationsLabel",
        "UserNotificationsOn",
        "UserNotificationsOff",
        "UserNotificationsNeedAddress",
        "UserDigestsNotActive",
        "UserSectionLanguage",
        "UserLanguageLabel",
        "UserSectionAddress",
        "UserAddressNone",
        "UserAddressVerified",
        "UserAddressUnverified",
        "UserAddressPending",
        "UserAddressNewLabel",
        "UserAddressSend",
        "UserAddressCancel",
        "UserAddressUnavailable",
        "UserAddressSent",
        "UserAddressSentUncertain",
        "UserAddressCancelled",
        "UserErrInvalidAddress",
        "UserErrAlreadyVerified",
        "UserErrRateLimited",
        "UserErrSendFailed",
        "UserErrInvalidValue",
        "UserErrGeneric",
        "UserSaved",
        "UserSectionStatus",
        "UserStatusLastAccepted",
        "UserStatusNever",
        "UserStatusLastAttempt",
        "UserStatusAccepted",
        "UserStatusFailed",
        "UserStatusUncertain",
        "UserStatusNextRun",
        "UserStatusNextUnknown",
        "UserStatusDisclaimer",
        "UserHelp",
        "LinkVerifyTitle",
        "LinkVerifyText",
        "LinkVerifyButton",
        "LinkVerifyDone",
        "LinkVerifyInvalid",
        "LinkVerifyNotApplied",
        "LinkUnsubscribeTitle",
        "LinkUnsubscribeText",
        "LinkUnsubscribeButton",
        "LinkUnsubscribeDone",
        "LinkUnsubscribeInvalid",
        "LinkError",
        "LinkNoToken",
        "LinkWorking",
        "LinkOpenPreferences"
    };
}
