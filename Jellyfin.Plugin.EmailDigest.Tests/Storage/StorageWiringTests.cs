using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.EmailDigest.Configuration;
using Jellyfin.Plugin.EmailDigest.Delivery;
using Jellyfin.Plugin.EmailDigest.Models;
using Jellyfin.Plugin.EmailDigest.Services;
using Jellyfin.Plugin.EmailDigest.Storage;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.EmailDigest.Tests.Storage;

/// <summary>
/// Pins that the plugin's real service registration puts every state file in the stable data
/// folder, not in the folder Jellyfin computes for the plugin.
/// </summary>
[Collection(PluginInstanceCollection.Name)]
public sealed class StorageWiringTests : IDisposable
{
    private readonly string _work = Path.Combine(Path.GetTempPath(), "EmailDigestWiring_" + Guid.NewGuid());

    public void Dispose()
    {
        Directory.Delete(_work, recursive: true);
    }

    [Fact]
    public async Task State_IsWrittenUnderTheServerDataDirectory_AndLegacyIsMigratedOnFirstResolve()
    {
        var data = Path.Combine(_work, "data");
        var plugins = Path.Combine(_work, "plugins");
        Directory.CreateDirectory(data);
        var paths = new Mock<IApplicationPaths>();
        paths.Setup(p => p.DataPath).Returns(data);
        paths.Setup(p => p.PluginsPath).Returns(plugins);
        paths.Setup(p => p.PluginConfigurationsPath).Returns(Path.Combine(_work, "configs"));
        var serializer = new Mock<IXmlSerializer>();
        serializer.Setup(x => x.DeserializeFromFile(typeof(PluginConfiguration), It.IsAny<string>())).Returns(() => new PluginConfiguration());
        var plugin = new Plugin(paths.Object, serializer.Object, NullLogger<Plugin>.Instance);

        Directory.CreateDirectory(plugin.DataFolderPath);
        await File.WriteAllTextAsync(Path.Combine(plugin.DataFolderPath, "checkpoint.json"), "{\"LastSuccessfulRunUtc\":\"2026-01-02T03:04:05+00:00\"}");

        var services = new ServiceCollection();
        services.AddSingleton(paths.Object);
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        new ServiceRegistrator().RegisterServices(services, new Mock<IServerApplicationHost>().Object);
        using var provider = services.BuildServiceProvider();

        var storage = provider.GetRequiredService<IPluginStorage>();
        Assert.Equal(Path.Combine(data, "EmailDigest"), storage.Root);
        Assert.NotEqual(plugin.DataFolderPath, storage.Root);

        var legacyHint = await provider.GetRequiredService<IDeliveryStateStore>().GetLegacyCheckpointAsync(CancellationToken.None);
        Assert.Equal(new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero), legacyHint);

        await provider.GetRequiredService<IUserPreferenceRepository>().SaveAsync(
            new UserNotificationPreferences { UserId = Guid.NewGuid(), Email = "a@example.com" },
            CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(storage.Root, "user-preferences.json")));
        Assert.False(File.Exists(Path.Combine(plugin.DataFolderPath, "user-preferences.json")));
    }

    [Fact]
    public void SubscriptionService_AndItsParts_ResolveFromTheRealRegistration()
    {
        var data = Path.Combine(_work, "data2");
        Directory.CreateDirectory(data);
        var paths = new Mock<IApplicationPaths>();
        paths.Setup(p => p.DataPath).Returns(data);
        paths.Setup(p => p.PluginsPath).Returns(Path.Combine(_work, "plugins2"));
        paths.Setup(p => p.PluginConfigurationsPath).Returns(Path.Combine(_work, "configs2"));
        var serializer = new Mock<IXmlSerializer>();
        serializer.Setup(x => x.DeserializeFromFile(typeof(PluginConfiguration), It.IsAny<string>())).Returns(() => new PluginConfiguration());
        _ = new Plugin(paths.Object, serializer.Object, NullLogger<Plugin>.Instance);

        var services = new ServiceCollection();
        services.AddSingleton(paths.Object);
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        services.AddSingleton(new Mock<MediaBrowser.Controller.Library.IUserManager>().Object);
        services.AddSingleton(new Mock<MediaBrowser.Model.Tasks.ITaskManager>().Object);
        new ServiceRegistrator().RegisterServices(services, new Mock<IServerApplicationHost>().Object);
        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<Jellyfin.Plugin.EmailDigest.Subscription.ISubscriptionService>());
        Assert.NotNull(provider.GetRequiredService<Jellyfin.Plugin.EmailDigest.Subscription.IUnsubscribeTokens>());
        Assert.NotNull(provider.GetRequiredService<Jellyfin.Plugin.EmailDigest.Verification.IAddressVerificationService>());
        Assert.NotNull(provider.GetRequiredService<IDeliveryStatusStore>());
        Assert.NotNull(provider.GetRequiredService<IScheduleInfo>());
    }
}
