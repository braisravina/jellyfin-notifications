namespace Jellyfin.Plugin.EmailDigest.Services;

/// <summary>
/// What is wrong with one configured monitored library.
/// </summary>
public enum MonitoredLibraryIssueKind
{
    /// <summary>
    /// The id is empty (<see cref="System.Guid.Empty"/>), which is what an unset or unparseable id becomes.
    /// </summary>
    InvalidId = 0,

    /// <summary>
    /// No Jellyfin library has this id, for example because the library was deleted.
    /// </summary>
    NotFound = 1,

    /// <summary>
    /// The same library is configured more than once.
    /// </summary>
    Duplicate = 2
}
