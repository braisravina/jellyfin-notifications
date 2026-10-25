using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Jellyfin.Plugin.EmailDigest.Services;

/// <summary>
/// Turns library issues into short, secret-free messages for validation errors.
/// </summary>
public static class MonitoredLibraryIssueText
{
    /// <summary>
    /// Describes issues.
    /// </summary>
    /// <param name="issues">The issues.</param>
    /// <returns>One message per issue.</returns>
    public static IReadOnlyList<string> Describe(IEnumerable<MonitoredLibraryIssue> issues)
    {
        return issues.Select(issue => string.Format(
            CultureInfo.InvariantCulture,
            "MonitoredLibraries[{0}] {1}",
            issue.Index,
            issue.Kind switch
            {
                MonitoredLibraryIssueKind.InvalidId => "has no valid library id.",
                MonitoredLibraryIssueKind.NotFound => "refers to a library that does not exist (it may have been deleted).",
                MonitoredLibraryIssueKind.Duplicate => "lists a library that is already listed.",
                _ => "is invalid."
            })).ToList();
    }
}
