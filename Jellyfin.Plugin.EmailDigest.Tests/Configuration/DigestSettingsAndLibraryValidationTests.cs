using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jellyfin.Plugin.EmailDigest.Configuration;
using Jellyfin.Plugin.EmailDigest.Digest;
using Jellyfin.Plugin.EmailDigest.Models;
using Jellyfin.Plugin.EmailDigest.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Plugin.EmailDigest.Tests.Configuration;

[Collection(PluginInstanceCollection.Name)]
public sealed class DigestSettingsAndLibraryValidationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "emaildigest-libs-" + Guid.NewGuid().ToString("N"));

    public DigestSettingsAndLibraryValidationTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
    }

    // Safe defaults

    [Fact]
    public void Defaults_AreSafe_DisabledAndDryRun()
    {
        var config = new PluginConfiguration();

        Assert.False(config.DigestEnabled);
        Assert.True(config.DigestDryRun);
        Assert.Equal(25, config.MaxEntriesPerSection);
        Assert.Equal(100, config.MaxEntriesPerDigest);
        Assert.Equal(10, config.MaxEpisodesPerSeries);
        Assert.Empty(PluginConfigurationValidator.Validate(config));
    }

    [Fact]
    public void ConfigurationSavedByAnOlderVersion_LoadsWithDryRunOn()
    {
        const string Xml = "<?xml version=\"1.0\"?><PluginConfiguration><DigestEnabled>true</DigestEnabled></PluginConfiguration>";

        var config = (PluginConfiguration?)new System.Xml.Serialization.XmlSerializer(typeof(PluginConfiguration)).Deserialize(new StringReader(Xml));

        Assert.True(config!.DigestEnabled);
        Assert.True(config.DigestDryRun);
        Assert.Equal(100, config.MaxEntriesPerDigest);
    }

    // Limits

    [Theory]
    [InlineData(0, 100, 10)]
    [InlineData(201, 100, 10)]
    [InlineData(25, 0, 10)]
    [InlineData(25, 501, 10)]
    [InlineData(25, 100, 0)]
    [InlineData(25, 100, 51)]
    [InlineData(-1, -1, -1)]
    public void Limits_OutOfRange_AreRejected(int perSection, int perDigest, int episodes)
    {
        var config = new PluginConfiguration { MaxEntriesPerSection = perSection, MaxEntriesPerDigest = perDigest, MaxEpisodesPerSeries = episodes };

        Assert.NotEmpty(PluginConfigurationValidator.Validate(config));
    }

    [Theory]
    [InlineData(1, 1, 1)]
    [InlineData(200, 500, 50)]
    public void Limits_AtTheBoundaries_AreAccepted(int perSection, int perDigest, int episodes)
    {
        var config = new PluginConfiguration { MaxEntriesPerSection = perSection, MaxEntriesPerDigest = perDigest, MaxEpisodesPerSeries = episodes };

        Assert.Empty(PluginConfigurationValidator.Validate(config));
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(720, true)]
    [InlineData(721, false)]
    public void Lookback_IsBoundedToThirtyDays(int hours, bool valid)
    {
        var config = new PluginConfiguration { FallbackLookbackHours = hours };

        Assert.Equal(valid, !PluginConfigurationValidator.Validate(config).Any(e => e.Contains("FallbackLookbackHours", StringComparison.Ordinal)));
    }

    [Fact]
    public void DigestOptions_FromConfiguration_CarriesTheLimits()
    {
        var options = DigestOptions.From(new PluginConfiguration { MaxEntriesPerSection = 7, MaxEntriesPerDigest = 21, MaxEpisodesPerSeries = 3 });

        Assert.Equal(7, options.MaxEntriesPerSection);
        Assert.Equal(21, options.MaxEntriesPerDigest);
        Assert.Equal(3, options.MaxEpisodesPerSeries);
    }

    [Fact]
    public void EpisodeLimit_FlowsFromTheOptionsThroughTheModelToTheRenderedText()
    {
        var items = Enumerable.Range(1, 8).Select(i => new DiscoveredMediaItem
        {
            ItemId = Guid.NewGuid(),
            ItemType = DiscoveredMediaItemType.Episode,
            Name = "E" + i,
            SeriesName = "Show",
            SeasonNumber = 1,
            EpisodeNumber = i,
            Category = MediaCategory.Series
        }).ToList();

        var model = new DigestBuilder().Build(items, new DigestOptions { MaxEpisodesPerSeries = 3 });
        var text = new DigestRenderer(new LocalizationService()).Render(model, new DigestRecipient("Ana", "en")).TextBody;

        Assert.Equal(3, model.EpisodeListLimit);
        Assert.Equal(3, text.Split('\n').Count(line => line.StartsWith("    - ", StringComparison.Ordinal)));
        Assert.Contains("* Show — 8 new episodes", text, StringComparison.Ordinal);
        Assert.Contains("and 5 more", text, StringComparison.Ordinal);
        Assert.Equal(8, model.Items.Count);
    }

    [Fact]
    public void ZeroEpisodeLimit_IsRejectedByTheOptions()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DigestBuilder().Build(Array.Empty<DiscoveredMediaItem>(), new DigestOptions { MaxEpisodesPerSeries = 0 }));
    }

    // Library validation on save, through the real Plugin

    private static IXmlSerializer Serializer()
    {
        var serializer = new Mock<IXmlSerializer>();
        serializer.Setup(x => x.DeserializeFromFile(typeof(PluginConfiguration), It.IsAny<string>())).Returns(() => new PluginConfiguration());
        return serializer.Object;
    }

    private Plugin CreatePlugin(Mock<IMonitoredLibraryValidator>? validator)
    {
        var paths = new Mock<IApplicationPaths>();
        paths.Setup(p => p.PluginsPath).Returns(Path.Combine(_root, "plugins"));
        paths.Setup(p => p.PluginConfigurationsPath).Returns(Path.Combine(_root, "configs"));
        var services = new ServiceCollection();
        if (validator is not null)
        {
            services.AddSingleton(validator.Object);
        }

        return new Plugin(paths.Object, Serializer(), NullLogger<Plugin>.Instance, services.BuildServiceProvider());
    }

    private static Mock<IMonitoredLibraryValidator> ValidatorReturning(params MonitoredLibraryIssue[] issues)
    {
        var validator = new Mock<IMonitoredLibraryValidator>();
        validator.Setup(v => v.Validate(It.IsAny<IReadOnlyList<MonitoredLibrary>>())).Returns(issues);
        return validator;
    }

    private static PluginConfiguration WithLibrary(Guid id, bool digestEnabled = false)
    {
        var config = new PluginConfiguration { DigestEnabled = digestEnabled };
        config.MonitoredLibraries.Add(new MonitoredLibrary { LibraryId = id, Category = MediaCategory.Movies });
        return config;
    }

    [Fact]
    public void ChangingLibraries_ToADeletedOne_IsRejectedAndNothingIsSaved()
    {
        var id = Guid.NewGuid();
        var plugin = CreatePlugin(ValidatorReturning(new MonitoredLibraryIssue(0, id, MonitoredLibraryIssueKind.NotFound)));

        var ex = Assert.Throws<PluginConfigurationValidationException>(() => plugin.UpdateConfiguration(WithLibrary(id)));

        Assert.Contains("does not exist", Assert.Single(ex.Errors), StringComparison.Ordinal);
        Assert.Empty(plugin.Configuration.MonitoredLibraries);
    }

    [Theory]
    [InlineData(MonitoredLibraryIssueKind.InvalidId)]
    [InlineData(MonitoredLibraryIssueKind.NotFound)]
    [InlineData(MonitoredLibraryIssueKind.Duplicate)]
    public void EveryIssueKind_IsRejectedAtSaveTime(MonitoredLibraryIssueKind kind)
    {
        var plugin = CreatePlugin(ValidatorReturning(new MonitoredLibraryIssue(0, Guid.NewGuid(), kind)));

        Assert.Throws<PluginConfigurationValidationException>(() => plugin.UpdateConfiguration(WithLibrary(Guid.NewGuid())));
    }

    [Fact]
    public void ValidLibraries_AreSaved()
    {
        var id = Guid.NewGuid();
        var plugin = CreatePlugin(ValidatorReturning());

        plugin.UpdateConfiguration(WithLibrary(id));

        Assert.Equal(id, Assert.Single(plugin.Configuration.MonitoredLibraries).LibraryId);
    }

    [Fact]
    public void EnablingTheDigest_RevalidatesEvenUnchangedLibraries()
    {
        var id = Guid.NewGuid();
        var validator = ValidatorReturning();
        var plugin = CreatePlugin(validator);
        plugin.UpdateConfiguration(WithLibrary(id));
        validator.Setup(v => v.Validate(It.IsAny<IReadOnlyList<MonitoredLibrary>>()))
            .Returns(new[] { new MonitoredLibraryIssue(0, id, MonitoredLibraryIssueKind.NotFound) });

        Assert.Throws<PluginConfigurationValidationException>(() => plugin.UpdateConfiguration(WithLibrary(id, digestEnabled: true)));
    }

    [Fact]
    public void UnrelatedSave_WithAStaleLibraryAndDigestOff_IsNotBlocked()
    {
        var id = Guid.NewGuid();
        var validator = ValidatorReturning();
        var plugin = CreatePlugin(validator);
        plugin.UpdateConfiguration(WithLibrary(id));
        validator.Setup(v => v.Validate(It.IsAny<IReadOnlyList<MonitoredLibrary>>()))
            .Returns(new[] { new MonitoredLibraryIssue(0, id, MonitoredLibraryIssueKind.NotFound) });
        var unrelated = WithLibrary(id);
        unrelated.SmtpHost = "smtp.example.com";

        plugin.UpdateConfiguration(unrelated);

        Assert.Equal("smtp.example.com", plugin.Configuration.SmtpHost);
    }

    [Fact]
    public void WhenJellyfinCannotBeAsked_TheLibraryChangeIsRefusedNotAcceptedUnverified()
    {
        var validator = new Mock<IMonitoredLibraryValidator>();
        validator.Setup(v => v.Validate(It.IsAny<IReadOnlyList<MonitoredLibrary>>())).Throws(new InvalidOperationException("db locked"));
        var plugin = CreatePlugin(validator);

        var ex = Assert.Throws<PluginConfigurationValidationException>(() => plugin.UpdateConfiguration(WithLibrary(Guid.NewGuid())));

        Assert.DoesNotContain("db locked", string.Join(" ", ex.Errors), StringComparison.Ordinal);
        Assert.Empty(plugin.Configuration.MonitoredLibraries);
    }

    [Fact]
    public void NoLibraries_NeedsNoJellyfinLookup()
    {
        var validator = ValidatorReturning();
        var plugin = CreatePlugin(validator);

        plugin.UpdateConfiguration(new PluginConfiguration { DigestEnabled = true });

        validator.Verify(v => v.Validate(It.IsAny<IReadOnlyList<MonitoredLibrary>>()), Times.Never);
    }

    [Fact]
    public void RejectedLibraryChange_DoesNotClearAStoredPasswordOrApplyOtherChanges()
    {
        var id = Guid.NewGuid();
        var plugin = CreatePlugin(ValidatorReturning(new MonitoredLibraryIssue(0, id, MonitoredLibraryIssueKind.NotFound)));
        var change = WithLibrary(id);
        change.SmtpHost = "other.example.com";

        Assert.Throws<PluginConfigurationValidationException>(() => plugin.UpdateConfiguration(change));

        Assert.Equal(string.Empty, plugin.Configuration.SmtpHost);
    }
}
