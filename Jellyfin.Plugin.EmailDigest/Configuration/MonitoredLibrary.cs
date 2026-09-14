using System;

namespace Jellyfin.Plugin.EmailDigest.Configuration;

/// <summary>
/// Maps a single Jellyfin library the admin has chosen to monitor to the digest section it
/// should be grouped under.
/// </summary>
public class MonitoredLibrary
{
    /// <summary>
    /// Gets or sets the Jellyfin library (virtual folder) id.
    /// </summary>
    public Guid LibraryId { get; set; }

    /// <summary>
    /// Gets or sets the digest section this library's new items are grouped under.
    /// </summary>
    public MediaCategory Category { get; set; }
}
