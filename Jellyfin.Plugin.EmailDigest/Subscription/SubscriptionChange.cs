namespace Jellyfin.Plugin.EmailDigest.Subscription;

/// <summary>
/// The result of changing a user's own preference.
/// </summary>
public enum SubscriptionChange
{
    /// <summary>The change was saved.</summary>
    Saved,

    /// <summary>The value was not acceptable; nothing changed.</summary>
    InvalidValue,

    /// <summary>The user does not exist or is disabled; nothing changed.</summary>
    UserUnavailable,

    /// <summary>Stored preferences could not be read or written; nothing changed.</summary>
    StorageError
}
