namespace Jellyfin.Plugin.EmailDigest.Storage;

/// <summary>
/// Where the plugin keeps its state.
/// </summary>
public interface IPluginStorage
{
    /// <summary>
    /// Gets the folder holding all plugin state. It lies under the server's data directory, outside
    /// the plugin installation tree, so installing, upgrading, disabling or removing a plugin version
    /// never touches it.
    /// </summary>
    string Root { get; }
}
