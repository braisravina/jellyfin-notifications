using Xunit;

namespace Jellyfin.Plugin.EmailDigest.Tests;

/// <summary>
/// Tests that construct a <see cref="Plugin"/> set its static <see cref="Plugin.Instance"/>; they run
/// one at a time so none of them observes another's instance.
/// </summary>
[CollectionDefinition(Name)]
public sealed class PluginInstanceCollection
{
    public const string Name = "PluginInstance";
}
