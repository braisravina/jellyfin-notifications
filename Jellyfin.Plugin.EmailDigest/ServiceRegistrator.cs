using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.EmailDigest;

/// <summary>
/// Registers Email Digest services with Jellyfin's dependency injection container.
/// </summary>
/// <remarks>
/// Discovered and invoked automatically by Jellyfin at server startup. Requires a
/// parameterless constructor. No services are registered yet; digest, media query,
/// email, checkpoint and preference services are added in later phases.
/// </remarks>
public class ServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
    }
}
