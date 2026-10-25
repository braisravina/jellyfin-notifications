using System;
using System.Collections.Generic;
using Jellyfin.Plugin.EmailDigest.Configuration;
using MediaBrowser.Controller.Library;

namespace Jellyfin.Plugin.EmailDigest.Services;

/// <inheritdoc />
/// <remarks>
/// Existence is taken from <see cref="ILibraryManager.GetVirtualFolders()"/>, the same list the
/// Jellyfin dashboard shows. An empty configuration is valid and means "monitor nothing": libraries
/// are an explicit allowlist, so a library added to Jellyfin later is never monitored until an
/// administrator lists it. A failure of Jellyfin itself is not caught here, so it can never be
/// mistaken for "every library was deleted".
/// </remarks>
public sealed class MonitoredLibraryValidator : IMonitoredLibraryValidator
{
    private readonly ILibraryManager _libraryManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="MonitoredLibraryValidator"/> class.
    /// </summary>
    /// <param name="libraryManager">Jellyfin's library manager.</param>
    public MonitoredLibraryValidator(ILibraryManager libraryManager)
    {
        ArgumentNullException.ThrowIfNull(libraryManager);
        _libraryManager = libraryManager;
    }

    /// <inheritdoc />
    public IReadOnlyList<MonitoredLibraryIssue> Validate(IReadOnlyList<MonitoredLibrary> monitoredLibraries)
    {
        ArgumentNullException.ThrowIfNull(monitoredLibraries);

        var issues = new List<MonitoredLibraryIssue>();
        if (monitoredLibraries.Count == 0)
        {
            return issues;
        }

        var existing = new HashSet<Guid>();
        foreach (var folder in _libraryManager.GetVirtualFolders())
        {
            // Entries with an unparseable id cannot be configured, so they are simply not candidates.
            if (Guid.TryParse(folder.ItemId, out var id) && id != Guid.Empty)
            {
                existing.Add(id);
            }
        }

        var seen = new HashSet<Guid>();
        for (var i = 0; i < monitoredLibraries.Count; i++)
        {
            var id = monitoredLibraries[i].LibraryId;
            if (id == Guid.Empty)
            {
                issues.Add(new MonitoredLibraryIssue(i, id, MonitoredLibraryIssueKind.InvalidId));
            }
            else if (!seen.Add(id))
            {
                issues.Add(new MonitoredLibraryIssue(i, id, MonitoredLibraryIssueKind.Duplicate));
            }
            else if (!existing.Contains(id))
            {
                issues.Add(new MonitoredLibraryIssue(i, id, MonitoredLibraryIssueKind.NotFound));
            }
        }

        return issues;
    }
}
