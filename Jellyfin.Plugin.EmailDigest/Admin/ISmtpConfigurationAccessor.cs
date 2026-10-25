using Jellyfin.Plugin.EmailDigest.Configuration;

namespace Jellyfin.Plugin.EmailDigest.Admin;

/// <summary>
/// Reads and saves the plugin configuration for the administrator service.
/// </summary>
public interface ISmtpConfigurationAccessor
{
    /// <summary>
    /// Gets a private copy of the current configuration; changing it affects nothing until saved.
    /// </summary>
    /// <returns>A copy of the configuration.</returns>
    PluginConfiguration GetCopy();

    /// <summary>
    /// Validates and saves a configuration.
    /// </summary>
    /// <param name="configuration">The configuration to save.</param>
    /// <exception cref="PluginConfigurationValidationException">It failed validation and was not saved.</exception>
    void Save(PluginConfiguration configuration);
}
