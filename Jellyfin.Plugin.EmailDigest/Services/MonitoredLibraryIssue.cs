using System;

namespace Jellyfin.Plugin.EmailDigest.Services;

/// <summary>
/// One problem found in the monitored-library configuration.
/// </summary>
/// <param name="Index">The position of the offending entry in the configured list.</param>
/// <param name="LibraryId">The configured library id.</param>
/// <param name="Kind">What is wrong with it.</param>
public readonly record struct MonitoredLibraryIssue(int Index, Guid LibraryId, MonitoredLibraryIssueKind Kind);
