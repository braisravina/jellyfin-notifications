using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.EmailDigest.Configuration;
using Jellyfin.Plugin.EmailDigest.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.EmailDigest.Tests.Configuration;

[Collection(PluginInstanceCollection.Name)]
public sealed class ConfigurationChangePolicyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "emaildigest-policy-" + Guid.NewGuid().ToString("N"));
    private string? _external;

    public ConfigurationChangePolicyTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
    }

    private static PluginConfiguration Config(string host = "smtp.example.com", int port = 587, string user = "mailer")
    {
        return new PluginConfiguration { SmtpHost = host, SmtpPort = port, SmtpUsername = user, SmtpSecurity = SmtpSecurityMode.StartTls };
    }

    [Fact]
    public void Unchanged_KeepsThePassword()
    {
        Assert.False(ConfigurationChangePolicy.MustClearStoredPassword(Config(), Config()));
    }

    [Fact]
    public void CaseAndWhitespaceOnlyHostDifference_KeepsThePassword()
    {
        Assert.False(ConfigurationChangePolicy.MustClearStoredPassword(Config(), Config(host: " SMTP.Example.COM ")));
    }

    [Theory]
    [InlineData("other.example.com", 587, "mailer")]
    [InlineData("smtp.example.com", 25, "mailer")]
    [InlineData("smtp.example.com", 587, "someone-else")]
    [InlineData("smtp.example.com", 587, "")]
    public void ChangedHostPortOrUsername_ClearsThePassword(string host, int port, string user)
    {
        Assert.True(ConfigurationChangePolicy.MustClearStoredPassword(Config(), Config(host, port, user)));
    }

    [Fact]
    public void OtherChanges_KeepThePassword()
    {
        var proposed = Config();
        proposed.SenderDisplayName = "Another name";
        proposed.SmtpTimeoutSeconds = 60;
        proposed.DigestEnabled = true;

        Assert.False(ConfigurationChangePolicy.MustClearStoredPassword(Config(), proposed));
    }

    // Through the real Plugin.UpdateConfiguration, which is also what Jellyfin's generic endpoint calls.

    // Jellyfin's real serializer returns a fresh default configuration when none is saved yet.
    private static IXmlSerializer Serializer()
    {
        var serializer = new Mock<IXmlSerializer>();
        serializer
            .Setup(x => x.DeserializeFromFile(typeof(PluginConfiguration), It.IsAny<string>()))
            .Returns(() => new PluginConfiguration());
        return serializer.Object;
    }

    private (Plugin Plugin, SmtpPasswordStore Store) CreatePlugin()
    {
        var paths = new Mock<IApplicationPaths>();
        paths.Setup(p => p.PluginsPath).Returns(Path.Combine(_root, "plugins"));
        paths.Setup(p => p.PluginConfigurationsPath).Returns(Path.Combine(_root, "configs"));
        var store = new SmtpPasswordStore(Path.Combine(_root, "data"), () => _external);
        var services = new ServiceCollection().AddSingleton<ISmtpPasswordStore>(store).BuildServiceProvider();
        var plugin = new Plugin(paths.Object, Serializer(), NullLogger<Plugin>.Instance, services);
        return (plugin, store);
    }

    [Fact]
    public async Task Plugin_ChangingTheHost_RemovesTheStoredPassword_ThroughTheGenericPath()
    {
        var (plugin, store) = CreatePlugin();
        plugin.UpdateConfiguration(Config());
        await store.SetAsync("saved-pass", CancellationToken.None);

        plugin.UpdateConfiguration(Config(host: "attacker.example.net"));

        Assert.False(store.GetStatus().IsSet);
        Assert.Equal("attacker.example.net", plugin.Configuration.SmtpHost);
    }

    [Fact]
    public async Task Plugin_UnrelatedChange_KeepsTheStoredPassword()
    {
        var (plugin, store) = CreatePlugin();
        plugin.UpdateConfiguration(Config());
        await store.SetAsync("saved-pass", CancellationToken.None);
        var next = Config();
        next.FallbackLookbackHours = 24;

        plugin.UpdateConfiguration(next);

        Assert.Equal("saved-pass", await store.ReadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Plugin_InvalidChange_IsRejectedAndTheStoredPasswordIsKept()
    {
        var (plugin, store) = CreatePlugin();
        plugin.UpdateConfiguration(Config());
        await store.SetAsync("saved-pass", CancellationToken.None);

        Assert.Throws<PluginConfigurationValidationException>(() => plugin.UpdateConfiguration(Config(host: "bad host name", port: 25)));

        Assert.Equal("saved-pass", await store.ReadAsync(CancellationToken.None));
        Assert.Equal("smtp.example.com", plugin.Configuration.SmtpHost);
    }

    [Fact]
    public async Task Plugin_ExternalPassword_IsNeverTouched()
    {
        var secret = Path.Combine(_root, "docker-secret");
        await File.WriteAllTextAsync(secret, "docker-pass");
        _external = secret;
        var (plugin, store) = CreatePlugin();
        plugin.UpdateConfiguration(Config());

        plugin.UpdateConfiguration(Config(host: "other.example.com"));

        Assert.Equal("docker-pass", await store.ReadAsync(CancellationToken.None));
    }

    [Fact]
    public void Plugin_WithoutAServiceProvider_StillSavesValidChanges()
    {
        var paths = new Mock<IApplicationPaths>();
        paths.Setup(p => p.PluginsPath).Returns(Path.Combine(_root, "plugins"));
        paths.Setup(p => p.PluginConfigurationsPath).Returns(Path.Combine(_root, "configs"));
        var plugin = new Plugin(paths.Object, Serializer(), NullLogger<Plugin>.Instance);

        plugin.UpdateConfiguration(Config());

        Assert.Equal("smtp.example.com", plugin.Configuration.SmtpHost);
    }
}
