using System;
using Jellyfin.Plugin.EmailDigest.Admin;
using Jellyfin.Plugin.EmailDigest.Delivery;
using Jellyfin.Plugin.EmailDigest.Digest;
using Jellyfin.Plugin.EmailDigest.Email;
using Jellyfin.Plugin.EmailDigest.Services;
using Jellyfin.Plugin.EmailDigest.Storage;
using Jellyfin.Plugin.EmailDigest.Subscription;
using Jellyfin.Plugin.EmailDigest.Verification;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Tasks;
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
        serviceCollection.AddSingleton<IPluginStorage>(provider => StorageMigrator.Prepare(
            provider.GetRequiredService<IApplicationPaths>().DataPath,
            GetLegacyDataFolderPath(),
            TimeProvider.System,
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>().CreateLogger("Jellyfin.Plugin.EmailDigest.Storage")));
        serviceCollection.AddSingleton<ILocalizationService, LocalizationService>();
        serviceCollection.AddSingleton<IUserPreferenceRepository>(p => new UserPreferenceRepository(StorageRoot(p)));
        serviceCollection.AddSingleton<IUserDirectory, JellyfinUserDirectory>();
        serviceCollection.AddSingleton<IRunSummaryStore>(p => new RunSummaryStore(StorageRoot(p)));
        serviceCollection.AddSingleton<IDigestRunLock>(p => new FileDigestRunLock(StorageRoot(p)));
        serviceCollection.AddSingleton<IDeliveryStateStore>(p => new DeliveryStateStore(StorageRoot(p), TimeProvider.System));
        serviceCollection.AddSingleton<ICheckpointService>(p => new CheckpointService(StorageRoot(p)));
        serviceCollection.AddSingleton<IDigestBuilder, DigestBuilder>();
        serviceCollection.AddSingleton<IDigestRenderer, DigestRenderer>();
        serviceCollection.AddSingleton<IMonitoredLibraryValidator, MonitoredLibraryValidator>();
        serviceCollection.AddSingleton<ISmtpPasswordStore>(p => new SmtpPasswordStore(StorageRoot(p)));
        serviceCollection.AddSingleton<IEmailTransport, MailKitEmailTransport>();
        serviceCollection.AddSingleton<ISmtpConfigurationAccessor, PluginConfigurationAccessor>();
        serviceCollection.AddSingleton<LibraryAdminService>();
        serviceCollection.AddSingleton<StatusAdminService>();
        serviceCollection.AddSingleton(provider => new DigestRunner(
            provider.GetRequiredService<ISmtpConfigurationAccessor>(),
            provider.GetRequiredService<IMonitoredLibraryValidator>(),
            provider.GetRequiredService<IUserPreferenceRepository>(),
            provider.GetRequiredService<IUserDirectory>(),
            provider.GetRequiredService<IMediaDiscoveryService>(),
            provider.GetRequiredService<IDigestBuilder>(),
            provider.GetRequiredService<IDigestRenderer>(),
            provider.GetRequiredService<IEmailTransport>(),
            provider.GetRequiredService<ISmtpPasswordStore>(),
            provider.GetRequiredService<IDeliveryStateStore>(),
            provider.GetRequiredService<IDeliveryStatusStore>(),
            provider.GetRequiredService<IUnsubscribeTokens>(),
            provider.GetRequiredService<IRunSummaryStore>(),
            provider.GetRequiredService<IDigestRunLock>(),
            DigestRetryPolicy.Default,
            TimeProvider.System,
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<DigestRunner>>()));
        serviceCollection.AddSingleton(provider => new SmtpAdminService(
            provider.GetRequiredService<ISmtpConfigurationAccessor>(),
            provider.GetRequiredService<ISmtpPasswordStore>(),
            provider.GetRequiredService<IEmailTransport>(),
            provider.GetRequiredService<ILocalizationService>(),
            TimeProvider.System,
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<SmtpAdminService>>()));
        serviceCollection.AddSingleton<IAddressVerificationService>(provider => new AddressVerificationService(
            StorageRoot(provider),
            () => VerificationPolicy.ForLifetime(provider.GetRequiredService<ISmtpConfigurationAccessor>().GetCopy().VerificationLinkMinutes),
            TimeProvider.System,
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>().CreateLogger("Jellyfin.Plugin.EmailDigest.Verification")));
        serviceCollection.AddSingleton<IDeliveryStatusStore>(provider => new DeliveryStatusStore(StorageRoot(provider)));
        serviceCollection.AddSingleton<IScheduleInfo>(provider => new JellyfinScheduleInfo(provider.GetRequiredService<ITaskManager>(), TimeProvider.System));
        serviceCollection.AddSingleton<IUnsubscribeTokens>(provider => new UnsubscribeTokenService(StorageRoot(provider)));
        serviceCollection.AddSingleton<VerificationMessageBuilder>();
        serviceCollection.AddSingleton<ISubscriptionService>(provider => new SubscriptionService(
            provider.GetRequiredService<IUserPreferenceRepository>(),
            provider.GetRequiredService<IAddressVerificationService>(),
            provider.GetRequiredService<IDeliveryStatusStore>(),
            provider.GetRequiredService<IScheduleInfo>(),
            provider.GetRequiredService<ISmtpConfigurationAccessor>(),
            provider.GetRequiredService<ISmtpPasswordStore>(),
            provider.GetRequiredService<IEmailTransport>(),
            provider.GetRequiredService<VerificationMessageBuilder>(),
            provider.GetRequiredService<IUnsubscribeTokens>(),
            provider.GetRequiredService<IUserDirectory>(),
            TimeProvider.System,
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<SubscriptionService>>()));
        serviceCollection.AddSingleton<ILibraryAccessResolver, JellyfinLibraryAccessResolver>();
        serviceCollection.AddSingleton<IMediaDiscoveryService, MediaDiscoveryService>();
    }

    private static string StorageRoot(IServiceProvider provider)
    {
        return provider.GetRequiredService<IPluginStorage>().Root;
    }

    // Where earlier versions kept state. Read only, as the source of a copy; never written to.
    private static string GetLegacyDataFolderPath()
    {
        return Plugin.Instance?.DataFolderPath
            ?? throw new InvalidOperationException("Email Digest plugin instance is not initialized yet.");
    }
}
