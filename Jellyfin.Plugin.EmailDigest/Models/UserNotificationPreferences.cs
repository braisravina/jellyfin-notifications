using System;
using Jellyfin.Plugin.EmailDigest.Configuration;

namespace Jellyfin.Plugin.EmailDigest.Models;

/// <summary>
/// A single Jellyfin user's Email Digest preferences.
/// </summary>
/// <remarks>
/// Identified by the user's stable Jellyfin user id, never by a locally-invented id. Deliberately
/// holds nothing Jellyfin already owns (no password, no library permissions) — those stay looked
/// up from Jellyfin itself at digest time so this plugin can never drift out of sync with them.
/// </remarks>
public class UserNotificationPreferences
{
    /// <summary>
    /// Gets or sets the Jellyfin user id these preferences belong to.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Gets or sets the notification address: the one digests are sent to, once it is verified. It
    /// changes only when a replacement address has been confirmed, never when one is merely requested.
    /// </summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets when the owner of <see cref="Email"/> proved they can read mail sent to it.
    /// <see langword="null"/> means the address is unverified, which is how a record written by
    /// hand or by an earlier version is treated: it never receives a digest until verified.
    /// </summary>
    public DateTimeOffset? EmailVerifiedUtc { get; set; }

    /// <summary>
    /// Gets or sets the user's preferred digest language.
    /// </summary>
    public string Language { get; set; } = Localization.SupportedLanguages.Default;

    /// <summary>
    /// Gets or sets a value indicating whether this user wants digest emails at all.
    /// </summary>
    public bool NotificationsEnabled { get; set; }

    /// <summary>
    /// Determines whether a digest may be sent to this user's address: the user wants digests and the
    /// address is a valid, verified one. An address that is merely pending is never considered.
    /// </summary>
    /// <returns><see langword="true"/> when sending is allowed by these preferences.</returns>
    public bool CanReceiveDigest()
    {
        return NotificationsEnabled
               && EmailVerifiedUtc is not null
               && ConfigurationValidation.IsValidEmail(Email)
               && ConfigurationValidation.IsValidSingleLine(Email, 254);
    }
}
