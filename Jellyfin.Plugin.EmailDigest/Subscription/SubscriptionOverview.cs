using System;
using Jellyfin.Plugin.EmailDigest.Delivery;

namespace Jellyfin.Plugin.EmailDigest.Subscription;

/// <summary>
/// Everything a user is shown about their own notifications. It holds no secret, no other user's
/// information, no internal path, no raw error text and no media identifier.
/// </summary>
/// <param name="NotificationsEnabled">Whether the user wants digests.</param>
/// <param name="Language">The language digests and emails are written in.</param>
/// <param name="Address">The user's own configured notification address, or empty.</param>
/// <param name="AddressVerified">Whether that address has been proved to be theirs.</param>
/// <param name="AddressVerifiedUtc">When it was verified.</param>
/// <param name="PendingAddress">A replacement address awaiting confirmation, or null.</param>
/// <param name="PendingExpiresUtc">When the confirmation link stops working.</param>
/// <param name="WillReceiveDigests">Whether, with the present settings, the user would be sent a digest: wanted, verified, and sending is on.</param>
/// <param name="DigestsActive">Whether the administrator has digests switched on for real sending.</param>
/// <param name="VerificationAvailable">Whether an address change can be confirmed.</param>
/// <param name="LastAcceptedUtc">When the SMTP server last accepted a digest for this user.</param>
/// <param name="LastAttemptUtc">When a digest was last attempted.</param>
/// <param name="LastAttemptStatus">How that attempt ended.</param>
/// <param name="NextRunUtc">When the digest task next runs, if it can be told. A run sends nothing when nothing is new.</param>
public sealed record SubscriptionOverview(
    bool NotificationsEnabled,
    string Language,
    string Address,
    bool AddressVerified,
    DateTimeOffset? AddressVerifiedUtc,
    string? PendingAddress,
    DateTimeOffset? PendingExpiresUtc,
    bool WillReceiveDigests,
    bool DigestsActive,
    bool VerificationAvailable,
    DateTimeOffset? LastAcceptedUtc,
    DateTimeOffset? LastAttemptUtc,
    DeliveryAttemptStatus? LastAttemptStatus,
    DateTimeOffset? NextRunUtc);
