using System.Collections.Generic;
using Jellyfin.Plugin.EmailDigest.Configuration;

namespace Jellyfin.Plugin.EmailDigest.Services;

/// <summary>
/// Checks the monitored-library configuration against the libraries Jellyfin actually has.
/// </summary>
/// <remarks>
/// This is a global, administrator-level check of the configuration. It says nothing about which
/// users may see a library: per-user access is decided at discovery time by
/// <see cref="ILibraryAccessResolver"/>. A library passing this check is merely eligible for
/// monitoring.
/// </remarks>
public interface IMonitoredLibraryValidator
{
    /// <summary>
    /// Validates the configured libraries.
    /// </summary>
    /// <param name="monitoredLibraries">The configured libraries.</param>
    /// <returns>The problems found, in configuration order; empty when the configuration is valid.</returns>
    IReadOnlyList<MonitoredLibraryIssue> Validate(IReadOnlyList<MonitoredLibrary> monitoredLibraries);
}
