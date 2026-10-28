using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Jellyfin.Plugin.EmailDigest.Admin;
using Jellyfin.Plugin.EmailDigest.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.EmailDigest.Tests.Admin;

/// <summary>
/// Pins that the copy the admin and status services work from carries every setting. A setting the
/// copy drops reads as its default, and a service that saves the copy would erase the stored value.
/// </summary>
[Collection(PluginInstanceCollection.Name)]
public sealed class PluginConfigurationAccessorTests : IDisposable
{
    private readonly string _work = Path.Combine(Path.GetTempPath(), "EmailDigestAccessor_" + Guid.NewGuid());

    public void Dispose()
    {
        if (Directory.Exists(_work))
        {
            Directory.Delete(_work, recursive: true);
        }
    }

    [Fact]
    public void GetCopy_CarriesEveryConfigurationProperty()
    {
        var plugin = NewPlugin();
        FillWithNonDefaultValues(plugin.Configuration);

        var copy = new PluginConfigurationAccessor().GetCopy();

        foreach (var property in typeof(PluginConfiguration).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.Name == nameof(PluginConfiguration.MonitoredLibraries))
            {
                continue;
            }

            Assert.True(
                Equals(property.GetValue(plugin.Configuration), property.GetValue(copy)),
                property.Name + " is not copied by PluginConfigurationAccessor.GetCopy");
        }

        Assert.Equal(
            plugin.Configuration.MonitoredLibraries.Select(l => (l.LibraryId, l.Category)),
            copy.MonitoredLibraries.Select(l => (l.LibraryId, l.Category)));
    }

    [Theory]
    [InlineData("http://localhost:18096", true, true)]
    [InlineData("http://127.0.0.1:18096/", true, true)]
    [InlineData("https://jellyfin.example.org", true, false)]
    [InlineData("not a url", false, false)]
    [InlineData("", false, false)]
    public void StoredPublicAddress_ReachesTheStatus(string stored, bool set, bool insecure)
    {
        var plugin = NewPlugin();
        plugin.Configuration.PublicBaseUrl = stored;

        var config = new PluginConfigurationAccessor().GetCopy();
        var normalized = PublicBaseUrl.TryNormalize(config.PublicBaseUrl);

        Assert.Equal(set, normalized is not null);
        Assert.Equal(insecure, normalized is not null && normalized.StartsWith("http://", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SavingAnotherSetting_KeepsThePublicAddressAndLifetime()
    {
        var plugin = NewPlugin();
        plugin.Configuration.PublicBaseUrl = "https://jellyfin.example.org";
        plugin.Configuration.VerificationLinkMinutes = 15;
        var accessor = new PluginConfigurationAccessor();

        var copy = accessor.GetCopy();
        copy.SmtpHost = "mail.example.test";
        accessor.Save(copy);

        Assert.Equal("https://jellyfin.example.org", plugin.Configuration.PublicBaseUrl);
        Assert.Equal(15, plugin.Configuration.VerificationLinkMinutes);
    }

    [Fact]
    public void Http_IsNeverUpgradedOrHidden_AndHostsOutsideTheUrlAreRefused()
    {
        // The test address is accepted as typed, so the status can warn that it is plain http.
        Assert.Equal("http://localhost:18096", PublicBaseUrl.TryNormalize("http://localhost:18096"));

        // Local-test support adds no new way in: credentials, query and fragment stay refused.
        Assert.Null(PublicBaseUrl.TryNormalize("http://user:pw@localhost:18096"));
        Assert.Null(PublicBaseUrl.TryNormalize("http://localhost:18096?next=http://evil.test"));
        Assert.Null(PublicBaseUrl.TryNormalize("http://localhost:18096/#x"));
        Assert.Null(PublicBaseUrl.TryNormalize("//localhost:18096"));
    }

    private Plugin NewPlugin()
    {
        var paths = new Mock<IApplicationPaths>();
        paths.Setup(p => p.DataPath).Returns(Path.Combine(_work, "data"));
        paths.Setup(p => p.PluginsPath).Returns(Path.Combine(_work, "plugins"));
        paths.Setup(p => p.PluginConfigurationsPath).Returns(Path.Combine(_work, "configs"));
        Directory.CreateDirectory(_work);
        var serializer = new Mock<IXmlSerializer>();
        serializer.Setup(x => x.DeserializeFromFile(typeof(PluginConfiguration), It.IsAny<string>())).Returns(() => new PluginConfiguration());
        return new Plugin(paths.Object, serializer.Object, NullLogger<Plugin>.Instance);
    }

    // Every writable property gets a value that differs from its default, so a dropped property shows.
    private static void FillWithNonDefaultValues(PluginConfiguration config)
    {
        foreach (var property in typeof(PluginConfiguration).GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanWrite))
        {
            var current = property.GetValue(config);
            object value = current switch
            {
                bool b => !b,
                int i => i + 7,
                string s => s + "x",
                Enum e => Enum.GetValues(e.GetType()).Cast<object>().First(v => !Equals(v, e)),
                _ => throw new InvalidOperationException("Unhandled configuration property type: " + property.Name)
            };
            property.SetValue(config, value);
        }

        config.MonitoredLibraries.Add(new MonitoredLibrary { LibraryId = Guid.NewGuid(), Category = MediaCategory.Series });
    }
}
