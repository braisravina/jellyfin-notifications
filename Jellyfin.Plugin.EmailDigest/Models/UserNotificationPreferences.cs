using System;

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
    /// Gets or sets the email address digests are sent to.
    /// </summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the user's preferred digest language.
    /// </summary>
    public string Language { get; set; } = Localization.SupportedLanguages.Default;

    /// <summary>
    /// Gets or sets a value indicating whether this user wants digest emails at all.
    /// </summary>
    public bool NotificationsEnabled { get; set; }
}
