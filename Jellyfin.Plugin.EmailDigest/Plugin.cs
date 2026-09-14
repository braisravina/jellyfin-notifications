using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Plugin.EmailDigest.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.EmailDigest;

/// <summary>
/// The main plugin entry point for Jellyfin Email Digest.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    private readonly ILogger<Plugin> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
    /// <param name="xmlSerializer">Instance of the <see cref="IXmlSerializer"/> interface.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{Plugin}"/> interface.</param>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer, ILogger<Plugin> logger)
        : base(applicationPaths, xmlSerializer)
    {
        _logger = logger;
        Instance = this;
    }

    /// <inheritdoc />
    public override string Name => "Email Digest";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("0e2dad96-d957-4026-8abb-0ca78068c5f0");

    /// <inheritdoc />
    public override string Description => "Sends scheduled multilingual email digests of newly added media to authorized Jellyfin users.";

    /// <summary>
    /// Gets the current plugin instance.
    /// </summary>
    public static Plugin? Instance { get; private set; }

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return
        [
            new PluginPageInfo
            {
                Name = Name,
                EmbeddedResourcePath = string.Format(CultureInfo.InvariantCulture, "{0}.Configuration.configPage.html", GetType().Namespace)
            }
        ];
    }

    /// <inheritdoc />
    /// <exception cref="PluginConfigurationValidationException">
    /// Thrown when <paramref name="configuration"/> fails validation. The invalid configuration
    /// is rejected and not persisted; the previously saved configuration remains in effect.
    /// </exception>
    public override void UpdateConfiguration(BasePluginConfiguration configuration)
    {
        var typedConfiguration = (PluginConfiguration)configuration;
        var errors = PluginConfigurationValidator.Validate(typedConfiguration);

        if (errors.Count > 0)
        {
            _logger.LogWarning("Rejected Email Digest configuration update: {ErrorCount} validation error(s)", errors.Count);
            throw new PluginConfigurationValidationException(errors);
        }

        base.UpdateConfiguration(configuration);
    }
}
