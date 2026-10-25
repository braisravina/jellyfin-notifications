using System;
using Jellyfin.Plugin.EmailDigest.Admin;
using Jellyfin.Plugin.EmailDigest.Delivery;
using Jellyfin.Plugin.EmailDigest.Digest;
using Jellyfin.Plugin.EmailDigest.Email;
using Jellyfin.Plugin.EmailDigest.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.EmailDigest;

/// <summary>
/// Registers Email Digest services with Jellyfin's dependency injection container.
/// </summary>
/// <remarks>
/// Discovered and invoked automatically by Jellyfin at server startup, before the DI container is
/// built and before <see cref="Plugin.Instance"/> is set. The factories below only read
/// <see cref="Plugin.Instance"/> when a registered service is actually resolved, which happens
/// later, after the container is built and the plugin instance exists — see
/// <see cref="IPluginServiceRegistrator"/>. Requires a parameterless constructor. Email and
/// digest orchestration services are added in later phases.
/// </remarks>
public class ServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<ILocalizationService, LocalizationService>();
        serviceCollection.AddSingleton<IUserPreferenceRepository>(_ => new UserPreferenceRepository(GetDataFolderPath()));
        serviceCollection.AddSingleton<IDeliveryStateStore>(_ => new DeliveryStateStore(GetDataFolderPath(), TimeProvider.System));
        serviceCollection.AddSingleton<ICheckpointService>(_ => new CheckpointService(GetDataFolderPath()));
        serviceCollection.AddSingleton<IDigestBuilder, DigestBuilder>();
        serviceCollection.AddSingleton<IDigestRenderer, DigestRenderer>();
        serviceCollection.AddSingleton<IMonitoredLibraryValidator, MonitoredLibraryValidator>();
        serviceCollection.AddSingleton<ISmtpPasswordStore>(_ => new SmtpPasswordStore(GetDataFolderPath()));
        serviceCollection.AddSingleton<IEmailTransport, MailKitEmailTransport>();
        serviceCollection.AddSingleton<ISmtpConfigurationAccessor, PluginConfigurationAccessor>();
        serviceCollection.AddSingleton(provider => new SmtpAdminService(
            provider.GetRequiredService<ISmtpConfigurationAccessor>(),
            provider.GetRequiredService<ISmtpPasswordStore>(),
            provider.GetRequiredService<IEmailTransport>(),
            provider.GetRequiredService<ILocalizationService>(),
            TimeProvider.System,
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<SmtpAdminService>>()));
        serviceCollection.AddSingleton<ILibraryAccessResolver, JellyfinLibraryAccessResolver>();
        serviceCollection.AddSingleton<IMediaDiscoveryService, MediaDiscoveryService>();
    }

    private static string GetDataFolderPath()
    {
        return Plugin.Instance?.DataFolderPath
            ?? throw new InvalidOperationException("Email Digest plugin instance is not initialized yet.");
    }
}
