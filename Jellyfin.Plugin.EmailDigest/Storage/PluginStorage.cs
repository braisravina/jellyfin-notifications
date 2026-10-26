namespace Jellyfin.Plugin.EmailDigest.Storage;

/// <inheritdoc />
public sealed class PluginStorage : IPluginStorage
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PluginStorage"/> class.
    /// </summary>
    /// <param name="root">The folder holding plugin state.</param>
    public PluginStorage(string root)
    {
        Root = root;
    }

    /// <inheritdoc />
    public string Root { get; }
}
