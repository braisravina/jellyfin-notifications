using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Jellyfin.Plugin.EmailDigest.Configuration;
using Jellyfin.Plugin.EmailDigest.Services;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.EmailDigest;

/// <summary>
/// The main plugin entry point for Jellyfin Email Digest.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    private readonly ILogger<Plugin> _logger;
    private readonly IServiceProvider? _services;

    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
    /// <param name="xmlSerializer">Instance of the <see cref="IXmlSerializer"/> interface.</param>
    /// <param name="logger">Instance of the <see cref="ILogger{Plugin}"/> interface.</param>
    /// <param name="services">
    /// The server's service provider, used lazily (never in the constructor) to reach the password
    /// store when a configuration change requires it.
    /// </param>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer, ILogger<Plugin> logger, IServiceProvider? services = null)
        : base(applicationPaths, xmlSerializer)
    {
        _logger = logger;
        _services = services;
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

        var libraryErrors = ValidateLibrariesIfNeeded(typedConfiguration);
        if (libraryErrors.Count > 0)
        {
            _logger.LogWarning("Rejected Email Digest configuration update: monitored libraries are not valid.");
            throw new PluginConfigurationValidationException(libraryErrors);
        }

        // A stored password must not outlive the server it was entered for. This runs before the
        // new configuration is saved, so a failure here leaves the old (matching) pair intact.
        if (ConfigurationChangePolicy.MustClearStoredPassword(Configuration, typedConfiguration))
        {
            var passwords = _services?.GetService<ISmtpPasswordStore>();
            if (passwords is not null && passwords.GetStatus().Source == SmtpPasswordSource.Stored)
            {
                passwords.ClearAsync(default).GetAwaiter().GetResult();
                _logger.LogWarning("The SMTP server settings changed, so the stored SMTP password was removed. Enter it again.");
            }
        }

        base.UpdateConfiguration(configuration);
    }

    private static bool SameLibraries(PluginConfiguration a, PluginConfiguration b)
    {
        return a.MonitoredLibraries.Count == b.MonitoredLibraries.Count
               && a.MonitoredLibraries.Zip(b.MonitoredLibraries).All(pair => pair.First.LibraryId == pair.Second.LibraryId && pair.First.Category == pair.Second.Category);
    }

    // Libraries are checked against Jellyfin when they change and whenever the digest is enabled, but
    // not on unrelated saves, so a library deleted in Jellyfin cannot block, say, fixing the SMTP host.
    // If Jellyfin cannot be asked, the change is refused rather than accepted unverified.
    private List<string> ValidateLibrariesIfNeeded(PluginConfiguration proposed)
    {
        var errors = new List<string>();
        var needed = proposed.MonitoredLibraries.Count > 0
                     && (proposed.DigestEnabled || !SameLibraries(Configuration, proposed));
        var validator = needed ? _services?.GetService<IMonitoredLibraryValidator>() : null;
        if (validator is null)
        {
            return errors;
        }

        try
        {
            errors.AddRange(MonitoredLibraryIssueText.Describe(validator.Validate(proposed.MonitoredLibraries.ToList())));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError("Monitored libraries could not be verified ({ExceptionType}).", ex.GetType().Name);
            errors.Add("The monitored libraries could not be verified against Jellyfin, so nothing was saved. Try again.");
        }

        return errors;
    }
}
